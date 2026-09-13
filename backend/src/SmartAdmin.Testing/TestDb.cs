using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace SmartAdmin.Testing;

/// <summary>
/// 集成测试的数据库选择。默认 SQLite(本地 <c>dotnet test</c> 不变);CI / 矩阵的其它腿通过环境变量切换:
/// <list type="bullet">
/// <item><c>SMART_TEST_DBTYPE=MySql</c> + <c>SMART_TEST_MYSQL=</c>(不含 Database 的服务器连接串)。</item>
/// <item><c>SMART_TEST_DBTYPE=SqlServer</c> + <c>SMART_TEST_SQLSERVER=</c>(不含 Database 的服务器连接串,
/// 需带 <c>TrustServerCertificate=True;Encrypt=False</c> —— MDS 4.x 默认加密且本地无受信证书)。</item>
/// <item><c>SMART_TEST_DBTYPE=PostgreSQL</c> + <c>SMART_TEST_POSTGRESQL=</c>。</item>
/// </list>
/// <para>库隔离:库名由 <c>identity</c> 确定性派生——同 identity → 同库(支持"同库二次启动"的幂等用例),
/// 不同 identity → 各自独立库。建库/删库经原始 <see cref="MySqlConnection"/> / <see cref="SqlConnection"/> /
/// <see cref="NpgsqlConnection"/> 连服务器(SqlSugar 不负责建库,CodeFirst 只建表)。</para>
/// <para>两条拿库的路:<see cref="ConnectionString"/> 给空库,宿主自己建表播种;<see cref="CloneFromTemplate"/>
/// 从模板库复制,省掉每个宿主的建表与播种(几百个起宿主的用例,PG 上这一段占每个用例一半以上)。
/// <see cref="AdminAppFactory{TEntryPoint}"/> 的默认形态走模板,裸容器用例仍拿空库。</para>
/// </summary>
public static class TestDb
{
    /// <summary>当前进程是否按 <c>SMART_TEST_DBTYPE=MySql</c> 切到 MySQL。</summary>
    public static bool UseMySql =>
        string.Equals(Environment.GetEnvironmentVariable("SMART_TEST_DBTYPE"), "MySql", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前进程是否按 <c>SMART_TEST_DBTYPE=SqlServer</c> 切到 SqlServer。</summary>
    public static bool UseSqlServer =>
        string.Equals(Environment.GetEnvironmentVariable("SMART_TEST_DBTYPE"), "SqlServer", StringComparison.OrdinalIgnoreCase);

    /// <summary>当前进程是否按 <c>SMART_TEST_DBTYPE=PostgreSQL</c> 切到 PostgreSQL。</summary>
    public static bool UsePostgreSql =>
        string.Equals(Environment.GetEnvironmentVariable("SMART_TEST_DBTYPE"), "PostgreSQL", StringComparison.OrdinalIgnoreCase);

    private static string MySqlBase =>
        Environment.GetEnvironmentVariable("SMART_TEST_MYSQL")
        ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;";

    private static string SqlServerBase =>
        Environment.GetEnvironmentVariable("SMART_TEST_SQLSERVER")
        ?? "Server=127.0.0.1;User ID=sa;Password=sa;TrustServerCertificate=True;Encrypt=False;";

    private static string PostgreSqlBase =>
        Environment.GetEnvironmentVariable("SMART_TEST_POSTGRESQL")
        ?? "Server=127.0.0.1;Port=5432;User ID=postgres;Password=postgres;";

    /// <summary>当前腿的 DbType 字符串(直接喂给 <c>AdminDatabaseOptions.DbType</c> / 配置)。</summary>
    public static string DbType => UseMySql ? "MySql" : UseSqlServer ? "SqlServer" : UsePostgreSql ? "PostgreSQL" : "Sqlite";

    /// <summary>隔离库名(由 identity 派生,合法标识符、稳定;MySQL 与 SqlServer 共用规则)。</summary>
    private static string DbName(string identity) =>
        "smart_it_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();

    /// <summary>解析连接串:SQLite 指向文件;MySQL/SqlServer/PostgreSQL 先建库(幂等)再返回带 Database 的连接串。</summary>
    public static string ConnectionString(string identity, string sqliteFile)
    {
        if (UseMySql)
        {
            var db = DbName(identity);
            ExecMySql($"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4;");
            return $"{MySqlBase.TrimEnd(';')};Database={db};";
        }
        if (UseSqlServer)
        {
            var db = DbName(identity);
            // CREATE DATABASE 须为批次内唯一语句,IF 是控制流不算另一条语句,该惯用法可用。
            ExecSqlServer($"IF DB_ID(N'{db}') IS NULL CREATE DATABASE [{db}];");
            return $"{SqlServerBase.TrimEnd(';')};Database={db};";
        }
        if (UsePostgreSql)
        {
            var db = DbName(identity);
            // PG 不支持 CREATE DATABASE IF NOT EXISTS(且 CREATE DATABASE 不能进事务/DO 块),先查 pg_database 再按需建(幂等)。
            if (ExecPostgreSqlScalar($"SELECT 1 FROM pg_database WHERE datname = '{db}'") is null)
                ExecPostgreSql($"CREATE DATABASE \"{db}\";");
            return $"{PostgreSqlBase.TrimEnd(';')};Database={db};";
        }
        return $"Data Source={sqliteFile}";
    }

    /// <summary>清理:SQLite 删文件;MySQL/SqlServer/PostgreSQL 删库(尽力而为)。</summary>
    public static void Cleanup(string identity, string sqliteFile)
    {
        if (UseMySql)
            try { ExecMySql($"DROP DATABASE IF EXISTS `{DbName(identity)}`;"); } catch { /* 尽力而为 */ }
        else if (UseSqlServer)
            try
            {
                // 释放连接池对目标库的占用,否则活动连接会阻塞 DROP;再断开其余会话后删库。
                SqlConnection.ClearAllPools();
                var db = DbName(identity);
                ExecSqlServer($"IF DB_ID(N'{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
            }
            catch { /* 尽力而为 */ }
        else if (UsePostgreSql)
            try
            {
                NpgsqlConnection.ClearAllPools();
                var db = DbName(identity);
                // 先踢掉目标库上的其余会话(否则活动连接阻塞 DROP),再删;PG 支持 DROP DATABASE IF EXISTS。
                ExecPostgreSql($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{db}' AND pid <> pg_backend_pid();");
                ExecPostgreSql($"DROP DATABASE IF EXISTS \"{db}\";");
            }
            catch { /* 尽力而为 */ }
        else
            try { if (File.Exists(sqliteFile)) File.Delete(sqliteFile); } catch { /* 尽力而为 */ }
    }

    // ── 模板库 ──────────────────────────────────────────────────────────────────────────────────
    // 本进程首次用到时建一次:起一个默认形态的宿主,让内核自己跑完 CodeFirst 与全部种子(含消费方实体与种子),
    // 再清掉运行期落下的行。之后 PG 用 CREATE DATABASE … TEMPLATE(文件级复制)、MySQL 逐表 CREATE TABLE … LIKE +
    // INSERT … SELECT、SQLite 直接复制文件。SqlServer 没有便宜的克隆手段(试过的都是空操作),仍走空库。

    private static readonly string TemplateIdentity = $"smart-it-template-{Environment.ProcessId}";
    private static readonly string TemplateSqliteFile = Path.Combine(Path.GetTempPath(), TemplateIdentity + ".db");
    private static Lazy<bool>? s_template;
    private static readonly Lock s_templateGate = new();

    /// <summary>
    /// 宿主跑起来就会落行的表:模板里必须是空的,否则每个副本都带着一份"活着的"租约、节点、会话。
    /// 消费方的实体也有这类"运行期落行"的表时,在跑第一个用例之前往这里追加(如模块初始化器里)。
    /// </summary>
    public static string[] RuntimeTables { get; set; } =
        ["sys_worker_lease", "sys_job_node", "sys_job_lock", "sys_job_log", "sys_session", "sys_login_log", "sys_op_log", "sys_exception_log"];

    /// <summary>
    /// 从模板克隆出测试库并返回连接串。同 identity 只克隆一次:库已存在就原样返回,"同库二次启动"的用例照旧成立。
    /// <paramref name="startTemplateHost"/> 只在本进程第一次调用时用到:收模板库的 identity(SQLite 上是文件路径),
    /// 起一个默认形态的宿主并返回它;宿主起来即 CodeFirst 与全部种子跑完,本方法随即释放它。
    /// </summary>
    public static string CloneFromTemplate(string identity, string sqliteFile, Func<string, IDisposable> startTemplateHost)
    {
        if (UseSqlServer) return ConnectionString(identity, sqliteFile);
        EnsureTemplate(startTemplateHost);   // 首次调用建模板;并发调用在这里等同一次

        if (UseMySql)
        {
            var db = DbName(identity);
            if (ExecMySqlScalar($"SELECT 1 FROM information_schema.schemata WHERE schema_name = '{db}'") is null)
            {
                var tpl = DbName(TemplateIdentity);
                ExecMySql($"CREATE DATABASE `{db}` CHARACTER SET utf8mb4;");
                foreach (var table in MySqlTables(tpl))
                {
                    ExecMySql($"CREATE TABLE `{db}`.`{table}` LIKE `{tpl}`.`{table}`;");
                    ExecMySql($"INSERT INTO `{db}`.`{table}` SELECT * FROM `{tpl}`.`{table}`;");
                }
            }
            return $"{MySqlBase.TrimEnd(';')};Database={db};";
        }
        if (UsePostgreSql)
        {
            var db = DbName(identity);
            if (ExecPostgreSqlScalar($"SELECT 1 FROM pg_database WHERE datname = '{db}'") is null)
            {
                var tpl = DbName(TemplateIdentity);
                try
                {
                    ExecPostgreSql($"CREATE DATABASE \"{db}\" TEMPLATE \"{tpl}\";");
                }
                catch (PostgresException ex) when (ex.SqlState == "55006")   // 模板上还挂着会话:踢掉再来一次
                {
                    ExecPostgreSql($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{tpl}' AND pid <> pg_backend_pid();");
                    ExecPostgreSql($"CREATE DATABASE \"{db}\" TEMPLATE \"{tpl}\";");
                }
            }
            return $"{PostgreSqlBase.TrimEnd(';')};Database={db};";
        }
        if (!File.Exists(sqliteFile)) File.Copy(TemplateSqliteFile, sqliteFile);
        return $"Data Source={sqliteFile}";
    }

    private static void EnsureTemplate(Func<string, IDisposable> startTemplateHost)
    {
        Lazy<bool> lazy;
        lock (s_templateGate)
            lazy = s_template ??= new Lazy<bool>(() => BuildTemplate(startTemplateHost), LazyThreadSafetyMode.ExecutionAndPublication);
        _ = lazy.Value;
    }

    private static bool BuildTemplate(Func<string, IDisposable> startTemplateHost)
    {
        var identity = UseMySql || UsePostgreSql ? TemplateIdentity : TemplateSqliteFile;
        using (startTemplateHost(identity)) { /* 宿主起来 = CodeFirst 与全部种子跑完 */ }

        ScrubRuntimeRows();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup(identity, TemplateSqliteFile);
        return true;
    }

    /// <summary>宿主已停:清掉运行期的行、断开池化连接,模板从此只读。</summary>
    private static void ScrubRuntimeRows()
    {
        if (UseMySql)
        {
            MySqlConnection.ClearAllPools();
            var tpl = DbName(TemplateIdentity);
            foreach (var table in RuntimeTables) ExecMySql($"DELETE FROM `{tpl}`.`{table}`;");
        }
        else if (UsePostgreSql)
        {
            NpgsqlConnection.ClearAllPools();
            var tpl = DbName(TemplateIdentity);
            using (var conn = new NpgsqlConnection($"{PostgreSqlBase.TrimEnd(';')};Database={tpl};Pooling=false;"))
            {
                conn.Open();
                foreach (var table in RuntimeTables) Exec(conn, $"DELETE FROM {table};");
            }
            // CREATE DATABASE … TEMPLATE 要求模板上没有任何会话
            ExecPostgreSql($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{tpl}' AND pid <> pg_backend_pid();");
        }
        else
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={TemplateSqliteFile};Pooling=False");
            conn.Open();
            foreach (var table in RuntimeTables) Exec(conn, $"DELETE FROM {table};");
            Exec(conn, "PRAGMA wal_checkpoint(TRUNCATE);");   // 让主文件自包含,复制一个文件就够
        }
    }

    private static void Exec(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<string> MySqlTables(string schema)
    {
        using var conn = new MySqlConnection(MySqlBase);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{schema}' AND table_type = 'BASE TABLE';";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static void ExecMySql(string sql)
    {
        using var conn = new MySqlConnection(MySqlBase);   // 连服务器(不指定 Database)以建/删库
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? ExecMySqlScalar(string sql)
    {
        using var conn = new MySqlConnection(MySqlBase);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void ExecSqlServer(string sql)
    {
        using var conn = new SqlConnection(SqlServerBase);   // 连 master(不指定 Database)以建/删库
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // PG 必须连到某个已存在的库才能建/删其它库,统一连维护库 postgres。
    private static string PostgreSqlAdmin => $"{PostgreSqlBase.TrimEnd(';')};Database=postgres;";

    private static void ExecPostgreSql(string sql)
    {
        using var conn = new NpgsqlConnection(PostgreSqlAdmin);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? ExecPostgreSqlScalar(string sql)
    {
        using var conn = new NpgsqlConnection(PostgreSqlAdmin);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
