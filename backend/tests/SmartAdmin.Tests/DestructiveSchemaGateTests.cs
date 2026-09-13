using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// CodeFirst 破坏性变更闸门。SqlSugar 的 InitTables 在 SQLite 之外的方言上会删掉实体没声明的列、
/// 按实体 ALTER 长度与可空性——数据就这么没了,谁也不会收到通知。闸门在扫描前比对实体与库,
/// 默认拒绝启动并点名到列,AllowDestructiveSchemaChange=true 才放行;SQLite 只加列,差异只打 Warning。
/// <para>判定规则在 <see cref="SchemaChangeDetector"/> 上单测(不依赖方言);启动路径按当前方言各锁一半。</para>
/// </summary>
public class DestructiveSchemaGateTests
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"smart-gate-{Guid.NewGuid():N}.db");

    private static ISqlSugarClient Db(AdminAppFactory f) => f.Services.GetRequiredService<ISqlSugarClient>();

    private static DbColumnInfo Col(string name, string type, int length = 0, bool nullable = false) =>
        new() { DbColumnName = name, DataType = type, Length = length, IsNullable = nullable };

    /// <summary>拿一张真实实体的列定义当"实体侧",库侧手工捏——判定规则本身与方言无关。</summary>
    private static (string Table, List<EntityColumnInfo> Columns) OpLogEntity(AdminAppFactory f)
    {
        var info = Db(f).EntityMaintenance.GetEntityInfo(typeof(SysOpLog));
        return (info.DbTableName, info.Columns.ToList());
    }

    private static EntityColumnInfo Ec(List<EntityColumnInfo> cols, string property) =>
        cols.First(c => c.PropertyName == property);

    /// <summary>把实体列原样"照"成库列(同名、同长、同可空、文本/数值类型对得上)= 无差异的基线。</summary>
    private static List<DbColumnInfo> Mirror(List<EntityColumnInfo> cols) =>
        cols.Where(c => !c.IsIgnore).Select(c => Col(
            c.DbColumnName,
            c.UnderType == typeof(string) ? "varchar" : c.UnderType == typeof(DateTime) ? "datetime" : "bigint",
            c.UnderType == typeof(string) ? c.Length : 0,
            c.IsNullable)).ToList();

    [Fact]
    public void 库列与实体一致时无差异()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);

        Assert.Empty(SchemaChangeDetector.Diff(table, cols, Mirror(cols)));
    }

    [Fact]
    public void 库里多出的列判为删列()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var db = Mirror(cols);
        db.Add(Col("dba_extra", "varchar", 10, nullable: true));

        var change = Assert.Single(SchemaChangeDetector.Diff(table, cols, db));
        Assert.Equal(SchemaChangeDetector.KindDrop, change.Kind);
        Assert.Equal("dba_extra", change.Column);
        Assert.Contains("varchar(10)", change.Detail);
    }

    [Fact]
    public void 字符串长度收窄判为收窄_放宽不算()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var ua = Ec(cols, nameof(SysOpLog.UserAgent));   // 实体 512
        var db = Mirror(cols);
        db.First(c => c.DbColumnName == ua.DbColumnName).Length = 1000;   // 库里 1000 → 实体 512 = 收窄

        var change = Assert.Single(SchemaChangeDetector.Diff(table, cols, db));
        Assert.Equal(SchemaChangeDetector.KindNarrow, change.Kind);
        Assert.Contains("1000", change.Detail);
        Assert.Contains("512", change.Detail);

        db.First(c => c.DbColumnName == ua.DbColumnName).Length = 100;    // 库里 100 → 实体 512 = 放宽,安全
        Assert.Empty(SchemaChangeDetector.Diff(table, cols, db));
    }

    [Fact]
    public void 库列长度未知或实体是大文本时不判收窄()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var db = Mirror(cols);
        // 实体 Length=0 的大文本列(ParamJson:varcharmax/longtext/text)对上库里 65535 的 text:不判
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.ParamJson)).DbColumnName).Length = 65535;
        // 库侧 Length 回 0 / -1(nvarchar(max)、SQLite TEXT):拿不准,不判
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.UserAgent)).DbColumnName).Length = -1;

        Assert.Empty(SchemaChangeDetector.Diff(table, cols, db));
    }

    [Fact]
    public void 可空改非空判为改为非空_主键除外()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var db = Mirror(cols);
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.Title)).DbColumnName).IsNullable = true;   // 实体 NOT NULL
        var id = db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.Id)).DbColumnName);
        id.IsNullable = true;   // 主键在库里被报成可空(部分方言元数据如此):不判
        id.IsPrimarykey = true;

        var change = Assert.Single(SchemaChangeDetector.Diff(table, cols, db));
        Assert.Equal(SchemaChangeDetector.KindNotNull, change.Kind);
        Assert.Equal(Ec(cols, nameof(SysOpLog.Title)).DbColumnName, change.Column);
    }

    [Fact]
    public void 文本与数值互换判为类型不兼容_其它类型差异不猜()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var db = Mirror(cols);
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.Title)).DbColumnName).DataType = "int";        // 字符串属性 ↔ int 列
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.ElapsedMs)).DbColumnName).DataType = "text";   // long 属性 ↔ text 列
        db.First(c => c.DbColumnName == Ec(cols, nameof(SysOpLog.Success)).DbColumnName).DataType = "tinyint";  // bool ↔ tinyint:同是数值,不判

        var changes = SchemaChangeDetector.Diff(table, cols, db);
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(SchemaChangeDetector.KindType, c.Kind));
    }

    [Fact]
    public void 列名不分大小写_改名前的旧列名不算多出()
    {
        using var f = new AdminAppFactory();
        var (table, cols) = OpLogEntity(f);
        var db = Mirror(cols);
        foreach (var c in db) c.DbColumnName = c.DbColumnName.ToUpperInvariant();

        Assert.Empty(SchemaChangeDetector.Diff(table, cols, db));
    }

    [Fact]
    public void 方言类型名归一()
    {
        Assert.True(SchemaChangeDetector.IsTextType("NVARCHAR"));
        Assert.True(SchemaChangeDetector.IsTextType("character varying"));
        Assert.True(SchemaChangeDetector.IsTextType("varchar(255)"));
        Assert.False(SchemaChangeDetector.IsTextType("uniqueidentifier"));
        Assert.True(SchemaChangeDetector.IsNumericOrTemporalType("timestamp without time zone"));
        Assert.True(SchemaChangeDetector.IsNumericOrTemporalType("INT"));
        Assert.False(SchemaChangeDetector.IsNumericOrTemporalType("json"));
    }

    // ── 启动路径 ──────────────────────────────────────────────────────

    /// <summary>
    /// 库里多出一列(DBA 自己加的)再启动:SQLite 上只打 Warning 且列还在;其它方言默认拒绝启动、点名到列,
    /// AllowDestructiveSchemaChange=true 才放行,放行后那列真的被 CodeFirst 删掉——这正是闸门存在的理由。
    /// </summary>
    [Fact]
    public void 库里多出的列_按方言拦或警告()
    {
        var dbPath = NewDbPath();
        try
        {
            string table;
            using (var f = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true })
            {
                _ = f.CreateClient();
                table = Db(f).EntityMaintenance.GetEntityInfo(typeof(SysOpLog)).DbTableName;
                Db(f).DbMaintenance.AddColumn(table, new DbColumnInfo { DbColumnName = "dba_extra", DataType = "varchar", Length = 10, IsNullable = true });
                Assert.True(ColumnExists(f, table, "dba_extra"));
            }

            if (TestDb.DbType == "Sqlite")
            {
                var log = new CaptureLoggerProvider();
                using var f = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true, Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
                _ = f.CreateClient();   // 起得来
                var line = Assert.Single(log.Entries, e => e.Text.Contains("破坏性差异", StringComparison.Ordinal));
                Assert.Equal(LogLevel.Warning, line.Level);
                Assert.Contains($"{table}.dba_extra", line.Text);
                Assert.Contains("删列", line.Text);
                Assert.True(ColumnExists(f, table, "dba_extra"));   // SQLite 只加列,没删
                return;
            }

            using (var f = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true })
            {
                var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
                Assert.Contains($"{table}.dba_extra", ex.Message);              // 哪张表哪一列
                Assert.Contains("删列", ex.Message);                              // 什么变更
                Assert.Contains("AllowDestructiveSchemaChange", ex.Message);    // 怎么办
            }
            using (var f = new AdminAppFactory
            {
                DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true,
                Settings = new Dictionary<string, string?> { ["SmartAdmin:Database:AllowDestructiveSchemaChange"] = "true" },
            })
            {
                _ = f.CreateClient();                                  // 放行
                Assert.False(ColumnExists(f, table, "dba_extra"));    // 列真的没了——所以默认得拦
            }
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    /// <summary>闸门骑在扫描上:CodeFirstVersion 未变、扫描跳过时,多出的列既不拦也不报——不给平时启动加开销。</summary>
    [Fact]
    public void 版本未变跳过扫描时不检查()
    {
        var dbPath = NewDbPath();
        var pinned = new Dictionary<string, string?> { ["SmartAdmin:Database:CodeFirstVersion"] = "v1" };
        try
        {
            string table;
            using (var f = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, Settings = pinned })
            {
                _ = f.CreateClient();
                table = Db(f).EntityMaintenance.GetEntityInfo(typeof(SysOpLog)).DbTableName;
                Db(f).DbMaintenance.AddColumn(table, new DbColumnInfo { DbColumnName = "dba_extra", DataType = "varchar", Length = 10, IsNullable = true });
            }

            var log = new CaptureLoggerProvider();
            using (var f = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, Settings = pinned, Overrides = s => s.AddSingleton<ILoggerProvider>(log) })
            {
                _ = f.CreateClient();   // 任何方言都起得来
                Assert.DoesNotContain(log.Entries, e => e.Text.Contains("破坏性", StringComparison.Ordinal));
                Assert.True(ColumnExists(f, table, "dba_extra"));
            }
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    private static bool ColumnExists(AdminAppFactory f, string table, string column) =>
        Db(f).DbMaintenance.GetColumnInfosByTableName(table, false)
            .Any(c => string.Equals(c.DbColumnName, column, StringComparison.OrdinalIgnoreCase));
}
