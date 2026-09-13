using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using SmartAdmin.Core;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// 首启数据库初始化(IHostedService,应用启动时执行一次):
/// 确保 SQLite 目录 → CodeFirst 建表(扫描已登记程序集的全部 [SugarTable] 实体)→ 执行全部种子。
/// <para>建表与种子都幂等:表已存在则按实体差异补列。SqlSugar 的 CodeFirst 在 SQLite 之外的方言上还会
/// 删掉实体没声明的列、按实体 ALTER 长度与可空性,所以执行前先过一道破坏性变更闸门
/// (<see cref="SchemaChangeDetector"/>,默认拒绝,<see cref="AdminDatabaseOptions.AllowDestructiveSchemaChange"/> 放行);
/// 种子按主键判存只插缺失行(见 <see cref="ISeedData{TEntity}"/>)。
/// 生产环境的建表开关见 <see cref="AdminDatabaseOptions.EnableCodeFirstInProduction"/>(默认关,需显式开启)。</para>
/// </summary>
internal sealed class DatabaseInitializer(
    ISqlSugarClient db,
    AdminDatabaseOptions options,
    SmartEntitySources sources,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment env,
    ILogger<DatabaseInitializer> logger,
    TimeProvider? time = null) : IHostedService   // 统一时间源;SqlSugar 层惯用 ?? TimeProvider.System 兜底
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SqlSugarSetup.EnsureSqliteDirectory(options.DbType, options.ConnectionString);

        // 生产建表安全闸门:生产环境即便 EnableCodeFirst=true,也需显式 EnableCodeFirstInProduction 才建表——
        // 生产库通常 DBA 手工维护,应用不应擅自 ALTER。非生产环境不受此约束。
        var codeFirstAllowed = options.EnableCodeFirst
            && (!env.IsProduction() || options.EnableCodeFirstInProduction);
        if (options.EnableCodeFirst && !codeFirstAllowed)
            logger.LogWarning("SmartAdmin: 生产环境未显式开启 EnableCodeFirstInProduction,已跳过 CodeFirst 自动建表。");

        // 扫描所有登记程序集中带 [SugarTable] 的非抽象类 —— 实体清单唯一来源
        var entityTypes = sources.Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetCustomAttribute<SugarTable>() is not null)
            .ToArray();

        var codeFirstRan = false;
        if (codeFirstAllowed)
        {
            var versionKey = CodeFirstVersionKey();
            if (versionKey is not null && await CodeFirstUpToDateAsync(versionKey, entityTypes))
            {
                logger.LogInformation("SmartAdmin: CodeFirstVersion {Key} 未变且实体表齐全,跳过 CodeFirst 建表扫描。", versionKey);
            }
            else
            {
                EnsureNoDestructiveSchemaChange(entityTypes);
                var (sqlCount, elapsedMs) = await RunCodeFirstAsync(entityTypes);
                if (versionKey is not null) await RecordCodeFirstVersionAsync(versionKey);
                codeFirstRan = true;
                logger.LogInformation("SmartAdmin: CodeFirst 建表完成({Count} 个实体,{Sql} 条 SQL,{Ms} ms:{Tables})",
                    entityTypes.Length, sqlCount, elapsedMs, string.Join(", ", entityTypes.Select(t => t.Name)));
            }
        }
        else
        {
            // 建表被跳过 ⇒ 没人替库补列。升级路径上这是最要命的一条,见方法注释。
            EnsureExistingTablesHaveEntityColumns(entityTypes);
        }

        // 种子与库就绪钩子共用一个作用域:两者都可能有 Scoped 依赖(仓储/Options),且钩子的语义就是"种子跑完之后"
        await using var readyScope = scopeFactory.CreateAsyncScope();
        string? storedVersion = null;
        var upgrading = false;

        if (options.EnableSeed)
        {
            var scope = readyScope;
            var seeds = scope.ServiceProvider.GetServices<ISeedData>().ToArray();
            // 建表被跳过时先探表:种子第一步就要读写表(SuperAdminSeed 在 HasData() 里就 SELECT sys_user),
            // 空库上撞过去只会得到驱动层的 "no such table",无从反推真正原因。
            if (!codeFirstAllowed) EnsureSeedTablesExist(seeds);

            // 升级闸门(见 ISeedData.SyncOnUpgrade):必须在跑种子**之前**读——SchemaVersionSeed 本身
            // 会把版本行插成 Current,读晚了空库和老库就分不出来了。
            // 空库:stored 为 null → upgrading=true,但空库上 UpdateList 本就是空,无副作用。
            storedVersion = (await db.Queryable<SysSchemaVersion>().FirstAsync(x => x.Id == 1))?.Version;
            upgrading = storedVersion != SysSchemaVersion.Current;

            // 本次启动共用同一个动态雪花地板(见 EnsureSeedIdsInReservedRange),避免种子循环跑久了
            // 跨毫秒导致同一次启动内前后判据不一致。
            var liveFloor = SnowflakeIdGenerator.CurrentFloor(time);

            var (inserted, synced) = (0, 0);
            foreach (var seed in seeds)
            {
                var (i, u) = await ExecuteSeedAsync(seed, upgrading, liveFloor);
                inserted += i;
                synced += u;
            }

            if (upgrading)
            {
                // 版本行落到 Current。SchemaVersionSeed 只在空库上插得进去(它自己不开 SyncOnUpgrade);
                // 该行版本落后于 Current 时须在这儿显式写回,否则下次启动又当成一次升级。
                await db.Updateable<SysSchemaVersion>()
                    .SetColumns(x => new SysSchemaVersion { Version = SysSchemaVersion.Current, AppliedTime = (time ?? TimeProvider.System).GetLocalNow().DateTime })
                    .Where(x => x.Id == 1)
                    .ExecuteCommandAsync();
                logger.LogInformation("SmartAdmin: 种子版本 {From} → {To}(结构性种子已同步 {Synced} 行)",
                    storedVersion ?? "(空库)", SysSchemaVersion.Current, synced);
            }

            logger.LogInformation("SmartAdmin: 种子执行完成(新插入 {Inserted} 行,升级同步 {Synced} 行)", inserted, synced);
        }

        // 消费者的"等库就绪再初始化":不必再自建托管服务并小心翼翼地排在 AddSmartAdmin 之后
        var context = new DatabaseReadyContext(codeFirstRan, options.EnableSeed, upgrading, storedVersion, SysSchemaVersion.Current);
        foreach (var hook in readyScope.ServiceProvider.GetServices<IDatabaseReadyHook>())
            await hook.OnDatabaseReadyAsync(context, cancellationToken);

        logger.LogInformation("SmartAdmin 数据库就绪:{DbType} / {Conn}", options.DbType, options.ConnectionString);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>CodeFirst 版本记录行(Id=1 是种子版本行,由 SchemaVersionSeed 维护)。</summary>
    private const long CodeFirstVersionRowId = 2;

    /// <summary>
    /// 跑一遍 CodeFirst,返回执行的 SQL 条数与耗时,慢在哪一眼能看出来。
    /// <para>建表语句默认各自独立自动提交 —— SqlServer/PostgreSQL 上每条 CREATE TABLE/INDEX 都各自落一次
    /// 事务日志,表数一多(实测 152 张表 10+ 分钟)绝大部分耗时都花在这上面,而不是
    /// DDL 本身。包一层显式事务,让全部语句只在最后 COMMIT 时落一次盘。MySQL 的 DDL 本身隐式自动
    /// 提交、事务包不住,这层对它是空操作,无害。</para>
    /// </summary>
    private async Task<(int SqlCount, long ElapsedMs)> RunCodeFirstAsync(Type[] entityTypes)
    {
        var sw = Stopwatch.StartNew();
        var sqlCount = 0;
        // 启动期还没有别的调用方,临时挂一个计数钩子再原样放回;DDL 与元数据查询都经 Ado 执行,都会计到。
        // Aop.OnLogExecuting 只有 setter,原值从连接配置的 AopEvents 上读(消费方自己挂的钩子不能被吃掉)
        var previous = db.CurrentConnectionConfig.AopEvents?.OnLogExecuting;
        db.Aop.OnLogExecuting = (sql, pars) =>
        {
            Interlocked.Increment(ref sqlCount);
            previous?.Invoke(sql, pars);
        };
        try
        {
            await db.RunInTransactionAsync(() =>
            {
                db.CodeFirst.InitTables(entityTypes);
                return Task.CompletedTask;
            });
        }
        finally
        {
            db.Aop.OnLogExecuting = previous!;
        }
        return (sqlCount, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// 破坏性变更闸门:CodeFirst 真要执行之前,逐张已存在的表比对实体列与库里的列
    /// (<see cref="SchemaChangeDetector"/>),挑出会丢数据的差异——实体没声明的列(会被 DROP)、
    /// 字符串长度收窄、可空改非空、文本与数值/时间互换。
    /// <para>默认拒绝启动并点名到列,<see cref="AdminDatabaseOptions.AllowDestructiveSchemaChange"/> 为 true 才放行。
    /// 为什么宁可现在就炸:放行的话 SqlSugar 会安静地把列删掉、把 varchar(255) 改成 varchar(100),数据就没了,
    /// 谁也不会收到通知。SQLite 例外——它的 CodeFirst 只加列,这些差异不会被执行,只打 Warning 让人知道。</para>
    /// <para>只在扫描真要跑时检查:CodeFirstVersion 未变时整个扫描都跳过,闸门也一并跳过,不给启动加开销。</para>
    /// </summary>
    private void EnsureNoDestructiveSchemaChange(Type[] entityTypes)
    {
        var existing = db.DbMaintenance.GetTableInfoList(false)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changes = new List<SchemaChangeDetector.Change>();
        foreach (var type in entityTypes)
        {
            var table = db.EntityMaintenance.GetTableName(type);
            if (!existing.Contains(table)) continue;   // 新表:只会 CREATE,没有可破坏的
            changes.AddRange(SchemaChangeDetector.Diff(
                table,
                db.EntityMaintenance.GetEntityInfo(type).Columns,
                db.DbMaintenance.GetColumnInfosByTableName(table, false)));
        }
        if (changes.Count == 0) return;

        var list = string.Join("; ", changes);
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            logger.LogWarning("SmartAdmin: 实体与库结构存在破坏性差异,SQLite 的 CodeFirst 只加列、不会执行它们,换到其它数据库前请处理:{Changes}", list);
            return;
        }
        if (options.AllowDestructiveSchemaChange)
        {
            logger.LogWarning("SmartAdmin: AllowDestructiveSchemaChange=true,CodeFirst 将执行以下破坏性变更:{Changes}", list);
            return;
        }

        throw new InvalidOperationException(
            $"SmartAdmin 启动失败:CodeFirst 将对库执行破坏性变更,已拒绝。差异清单(表.列: 变更(库里现状 → 实体要求)):{list}。" +
            "这些变更一旦执行数据就没了——实体没声明的列会被 DROP,长度收窄会截断,改非空会让含 NULL 的行 ALTER 失败。二选一:" +
            "(1) 确认清单无误(比如列确实该删、收窄的列里没有超长数据),配置 SmartAdmin:Database:AllowDestructiveSchemaChange=true 放行本次启动;" +
            "(2) 改回实体定义,或由 DBA 先手工处理数据再启动。" +
            "库里多出的列若想保留又不让 CodeFirst 删,可在实体上标 [SugarTable(IsDisabledDelete = true)]。详见文档站「部署」一节。");
    }

    /// <summary>
    /// 门控键:消费方的 <see cref="AdminDatabaseOptions.CodeFirstVersion"/> 加上内核自己的架构版本。
    /// 内核升级改了表结构会升 <see cref="SysSchemaVersion.Current"/>,键随之变化,不用消费方跟着改号。
    /// 没配就返回 null,即每次启动都扫。
    /// </summary>
    private string? CodeFirstVersionKey()
    {
        var configured = options.CodeFirstVersion?.Trim();
        if (string.IsNullOrEmpty(configured)) return null;
        if (configured.Length > 24)
            throw new InvalidOperationException($"SmartAdmin:Database:CodeFirstVersion 最长 24 个字符,当前 {configured.Length} 个:{configured}");
        return $"{SysSchemaVersion.Current}/{configured}";
    }

    /// <summary>
    /// 版本键没变、且每个实体的表都在,才算"不用扫"。查表清单只一条 SQL,远比逐表比对列便宜。
    /// 空库上版本表还不存在,读它会抛,视为"需要建表"——门控绝不能把首启拦下。
    /// </summary>
    private async Task<bool> CodeFirstUpToDateAsync(string versionKey, Type[] entityTypes)
    {
        string? stored;
        try
        {
            stored = (await db.Queryable<SysSchemaVersion>().FirstAsync(x => x.Id == CodeFirstVersionRowId))?.Version;
        }
        catch
        {
            return false;
        }
        if (stored != versionKey) return false;

        var tables = db.DbMaintenance.GetTableInfoList(false)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entityTypes.All(t => tables.Contains(db.EntityMaintenance.GetTableName(t)));
    }

    /// <summary>建表成功后记下版本键(Id=2 那行),下次启动据此跳过扫描。</summary>
    private async Task RecordCodeFirstVersionAsync(string versionKey)
    {
        var now = (time ?? TimeProvider.System).GetLocalNow().DateTime;
        var updated = await db.Updateable<SysSchemaVersion>()
            .SetColumns(x => new SysSchemaVersion { Version = versionKey, AppliedTime = now })
            .Where(x => x.Id == CodeFirstVersionRowId)
            .ExecuteCommandAsync();
        if (updated == 0)
            await db.Insertable(new SysSchemaVersion { Id = CodeFirstVersionRowId, Version = versionKey, AppliedTime = now })
                .ExecuteCommandAsync();
    }

    /// <summary>
    /// 建表被跳过(生产闸门关 / EnableCodeFirst=false)时的启动前置检查:种子要写的表必须已存在。
    /// 缺表就抛出可行动的错误,而不是让种子去撞驱动层的"表不存在"。
    /// <para>只探种子涉及的表,不探全部实体:全量探测会让今天能正常启动的配置(消费者自管部分表等)
    /// 变成起不来 —— 那是新的失败面,而种子表恰好覆盖了真正会崩的那条路径。</para>
    /// </summary>
    private void EnsureSeedTablesExist(IReadOnlyCollection<ISeedData> seeds)
    {
        // 表名取自 ISeedData<T> 的泛型实参,不调 HasData()——SuperAdminSeed 在 HasData() 里就查库了。
        var missing = seeds
            .Select(SeedEntityType)
            .Distinct()
            .Select(db.EntityMaintenance.GetTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // isCache:false —— 元数据缓存可能掩盖"表其实不存在";不 try/catch,连不上库应原样冒出而非误报为缺表
            .Where(table => !db.DbMaintenance.IsAnyTable(table, false))
            .ToArray();
        if (missing.Length == 0) return;   // 表齐 → 种子照常执行(DBA 手工建表的库,超管与菜单树仍须写入)

        throw new InvalidOperationException(
            $"SmartAdmin 启动失败:CodeFirst 自动建表已跳过(EnableCodeFirst={options.EnableCodeFirst}," +
            $"当前环境={env.EnvironmentName},EnableCodeFirstInProduction={options.EnableCodeFirstInProduction})," +
            $"但种子要写的表在库中不存在:{string.Join(", ", missing)}。二选一:" +
            "(1) 配置 SmartAdmin:Database:EnableCodeFirstInProduction=true,首启由应用建表;" +
            "(2) 先由 DBA 建好表结构再启动。详见 docs/deployment.md。" +
            "(若确实不需要内置种子——超管账号与菜单树——可配置 SmartAdmin:Database:EnableSeed=false。)");
    }

    /// <summary>
    /// 建表被跳过时的<b>升级</b>前置检查:已存在的表必须具备实体映射的全部列。
    /// <para>种子探表守卫 <see cref="EnsureSeedTablesExist"/> 只看<b>表在不在</b>。而生产按 deployment.md 是关着建表闸门的,
    /// 于是内核某个版本给已有表加了一列时:表在 → 探表守卫放行 → <b>启动成功</b> → 第一次查到那张表才炸在
    /// 驱动层的 "no such column" 上。没有表名、没有列名、没人知道该 ALTER 什么;
    /// <b>内核每加一列都会这样咬到所有关闸门的用户</b>。本检查把它变成一条点名到列的启动错误。</para>
    /// <para>只查<b>缺列</b>,不查类型/长度/可空性的变化:那些既难跨四种方言判准,又容易把今天能正常跑的库
    /// (DBA 有意把 varchar 放宽、加了自己的列)判成起不来 —— 那是新的失败面。而缺列是<b>确定会崩</b>的那一类:
    /// 实体映射了它,查询就一定会 SELECT 它。</para>
    /// <para>ponytail: 表不存在的情况留给种子探表那条路(见 <see cref="EnsureSeedTablesExist"/>),这里跳过 —— 不重复报同一件事。</para>
    /// </summary>
    private void EnsureExistingTablesHaveEntityColumns(Type[] entityTypes)
    {
        var drift = new List<string>();

        foreach (var type in entityTypes)
        {
            var table = db.EntityMaintenance.GetTableName(type);
            if (!db.DbMaintenance.IsAnyTable(table, false)) continue;      // 缺表:另一条路径管

            var actual = db.DbMaintenance.GetColumnInfosByTableName(table, false)
                .Select(c => c.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = db.EntityMaintenance.GetEntityInfo(type).Columns
                .Where(c => !c.IsIgnore && !actual.Contains(c.DbColumnName))
                .Select(c => c.DbColumnName)
                .ToArray();

            if (missing.Length > 0) drift.Add($"{table}({string.Join(", ", missing)})");
        }

        if (drift.Count == 0) return;

        throw new InvalidOperationException(
            $"SmartAdmin 启动失败:库表结构落后于当前实体,以下表缺少列:{string.Join("; ", drift)}。" +
            $"(CodeFirst 自动建表已跳过:EnableCodeFirst={options.EnableCodeFirst},当前环境={env.EnvironmentName}," +
            $"EnableCodeFirstInProduction={options.EnableCodeFirstInProduction} —— 于是没人替库补列。)" +
            "这通常意味着内核升级新增了列而库还是老结构。二选一:" +
            "(1) 本次启动配置 SmartAdmin:Database:EnableCodeFirstInProduction=true,由应用补列" +
            "(会丢数据的变更——删列、收窄、改非空——另有一道闸门默认拒绝,不会顺手执行);" +
            "(2) 由 DBA 对上述表执行 ALTER TABLE ... ADD COLUMN 补齐后再启动。" +
            "详见 docs/deployment.md 的升级一节。" +
            "现在拦下,是因为放行的话进程会正常启动,直到第一次查到这些表才炸在驱动层的\"列不存在\"上。");
    }

    /// <summary>种子的目标实体类型 = 其 <see cref="ISeedData{TEntity}"/> 的泛型实参</summary>
    private static Type SeedEntityType(ISeedData seed) => seed.GetType().GetInterfaces()
        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISeedData<>))
        .GetGenericArguments()[0];

    /// <summary>执行单个种子:经泛型接口取实体类型,反射进入强类型管道(仅启动期一次,开销可忽略)</summary>
    private async Task<(int Inserted, int Synced)> ExecuteSeedAsync(ISeedData seed, bool upgrading, long liveFloor)
    {
        var method = typeof(DatabaseInitializer)
            .GetMethod(nameof(ExecuteSeedCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(SeedEntityType(seed));

        return await (Task<(int, int)>)method.Invoke(this, [seed, upgrading, liveFloor])!;
    }

    /// <summary>
    /// 强类型种子管道:校验固定 Id → Storageable 按主键分流 → 插入缺失行(幂等核心);
    /// 升级时对结构性种子(<see cref="ISeedData{TEntity}.SyncOnUpgrade"/>)额外覆盖已有行。
    /// </summary>
    private async Task<(int Inserted, int Synced)> ExecuteSeedCoreAsync<TEntity>(ISeedData<TEntity> seed, bool upgrading, long liveFloor)
        where TEntity : PrimaryId, new()
    {
        var rows = seed.HasData().ToList();
        if (rows.Count == 0) return (0, 0);

        EnsureSeedIdsInReservedRange(seed, rows, liveFloor);
        EnsureSeedIdsUnique(seed, rows);

        // 连接表:代理主键会漂(运行时授权发雪花号),按业务唯一键判存——该键已存在就跳过,
        // 无论它挂的是种子 Id 还是雪花 Id。不这么做,种子会拿固定 Id 把同一业务键再插一遍 → 撞唯一索引 → 启动崩。
        // WhereColumns 让 Storageable 按这些列(而非主键)分流;这类种子无业务字段可覆盖,故不参与 SyncOnUpgrade。
        // (连接行按 RbacService 语义是物理删除,库里不存在软删行,故无需 ClearFilter。)
        if (seed.DedupColumns is { Length: > 0 } dedup)
        {
            var joinStorage = await db.Storageable(rows).WhereColumns(dedup).ToStorageAsync();
            var n = joinStorage.InsertList.Count == 0 ? 0 : await joinStorage.AsInsertable.ExecuteCommandAsync();
            return (n, 0);
        }

        // 按主键分流。不用 Storageable 而是自己查:存在性判断必须看**物理**行,
        // 而 ClearFilter 挂不到 Storageable 上 —— 全局软删过滤器会让用户软删掉的内置菜单查不到,
        // 判成"不存在"→ 走 INSERT → 撞主键(软删一条内置菜单,应用就再也起不来)。
        var ids = rows.Select(r => r.Id).ToList();
        var existing = await db.Queryable<TEntity>().ClearFilter()
            .Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var existingIds = existing.ToHashSet();

        var toInsert = rows.Where(r => !existingIds.Contains(r.Id)).ToList();
        var inserted = toInsert.Count == 0 ? 0 : await db.Insertable(toInsert).ExecuteCommandAsync();

        var toUpdate = rows.Where(r => existingIds.Contains(r.Id)).ToList();
        if (!upgrading || !seed.SyncOnUpgrade || toUpdate.Count == 0) return (inserted, 0);

        // IgnoreColumns 不是防御性代码:AsUpdateable 默认刷全部映射列,而种子实例的 CreateTime 是
        // default(DateTime)(0001-01-01)、CreateUserId 是 null —— 不排除就会把老库正确的审计字段刷成垃圾。
        // IsDelete 同理:用户软删掉的内置菜单不该被升级复活。
        // CreateOrgId(数据范围锚点)只在 IOrgScoped 实体(DataEntity / OrgAuditEntity)上存在;
        // 刷成 null 会让那些行从所有机构范围查询里消失,一并排除。按接口而非 DataEntity 基类判定,覆盖不软删的机构实体。
        // IsDelete 仅 BaseEntity 系有此列;AuditEntity 系无该列时 IgnoreColumns 找不到即跳过,无害,故无条件列入。
        string[] ignored = typeof(TEntity).IsAssignableTo(typeof(IOrgScoped))
            ? [nameof(AuditEntity.CreateTime), nameof(AuditEntity.CreateUserId), nameof(BaseEntity.IsDelete), nameof(IOrgScoped.CreateOrgId)]
            : [nameof(AuditEntity.CreateTime), nameof(AuditEntity.CreateUserId), nameof(BaseEntity.IsDelete)];

        // 声明了列白名单就只刷这几列(见 ISeedData.SyncColumns),否则按上面的排除法刷其余全部列
        var synced = seed.SyncColumns is { Length: > 0 } only
            ? await db.Updateable(toUpdate).UpdateColumns(only).ExecuteCommandAsync()
            : await db.Updateable(toUpdate).IgnoreColumns(ignored).ExecuteCommandAsync();

        return (inserted, synced);
    }

    /// <summary>
    /// 种子固定 Id 必须严格小于 <paramref name="liveFloor"/>(启动时刻的动态雪花地板,
    /// 见 <see cref="SnowflakeIdGenerator.CurrentFloor"/>)。
    /// <para>下界:Id=0 会被 AOP 当成"未指定"填上新雪花号,于是每次启动都判不存在 → 重复插入,幂等直接失效。</para>
    /// <para>上界:雪花号只会随时间单调增长,严格小于启动那一刻算出的地板值,就永远不会被本实例此后
    /// 真实产生的雪花号撞上。这一层是<b>唯一真正有牙的约束</b>:区间写在文档里没人读,写成启动异常才跑不掉。</para>
    /// <para>这里只校验"所有种子都不得越界"这条通用规则;"内核自己不得超过 <see cref="SmartSeedIds.KernelMax"/>"
    /// 是内核的自律,由 <c>SeedIdRangeTests</c> 守 —— 运行时不该去猜哪个种子是消费者的。</para>
    /// </summary>
    private static void EnsureSeedIdsInReservedRange<TEntity>(ISeedData<TEntity> seed, List<TEntity> rows, long liveFloor)
        where TEntity : PrimaryId, new()
    {
        var bad = rows.Where(r => r.Id < 1 || r.Id >= liveFloor).Select(r => r.Id).Distinct().ToArray();
        if (bad.Length == 0) return;

        throw new InvalidOperationException(
            $"种子 {seed.GetType().Name} 的固定 Id 越界:{string.Join(", ", bad)}。" +
            $"种子数据必须显式指定固定 Id 且严格小于当前雪花地板 {liveFloor}" +
            "(Id=0 会被审计 AOP 填成新雪花号,导致每次启动重复插入;" +
            $"≥ {liveFloor} 是雪花号从此刻起会真实产生的号段,迟早与新增数据主键冲突)。" +
            $"内核内置种子用 [1, {SmartSeedIds.KernelMax}],消费者种子请从 {SmartSeedIds.ConsumerMin} 起取号。");
    }

    /// <summary>本次启动已见过的种子固定 Id(按实体类型分桶,值=声领它的种子类名)——供跨种子撞号检测。</summary>
    private readonly Dictionary<Type, Dictionary<long, string>> _seenSeedIds = [];

    /// <summary>
    /// 种子固定 Id 不得重复:同种子内重复(复制行忘改 Id)与跨种子撞号(新种子挑了已占用的号)一律启动失败。
    /// <para>撞号的破坏是<b>静默</b>的:幂等判存把后来的行当"已存在"跳过(菜单树无声缺一块),
    /// 开了 <see cref="ISeedData{TEntity}.SyncOnUpgrade"/> 的种子升级时还会把别人的行覆盖掉——所以必须大声失败。</para>
    /// </summary>
    private void EnsureSeedIdsUnique<TEntity>(ISeedData<TEntity> seed, List<TEntity> rows)
        where TEntity : PrimaryId, new()
    {
        if (!_seenSeedIds.TryGetValue(typeof(TEntity), out var seen))
            _seenSeedIds[typeof(TEntity)] = seen = [];

        foreach (var row in rows)
        {
            if (seen.TryGetValue(row.Id, out var owner))
                throw new InvalidOperationException(
                    $"种子 Id 撞号:{seed.GetType().Name} 与 {owner} 都声领了 {typeof(TEntity).Name} 的 Id={row.Id}。" +
                    "固定 Id 在同一实体上必须全局唯一,请换一个未占用的号" +
                    $"(内核用 [1, {SmartSeedIds.KernelMax}],消费者从 {SmartSeedIds.ConsumerMin} 起取号)。");
            seen[row.Id] = seed.GetType().Name;
        }
    }
}
