using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// CodeFirstVersion 门控:配了版本号,建表成功后记进 sys_schema_version(Id=2),下次启动版本没变且实体表齐全
/// 就跳过 CodeFirst 扫描。消费方表多、库在远端时,这个逐表比对列定义的扫描是启动慢的大头。
/// 观察手段不看日志,看行为:跳过扫描 = 手工删掉的列不会被补回;重新扫描 = 补回来。
/// </summary>
public class CodeFirstVersionTests
{
    private static AdminAppFactory Factory(string dbPath, string? version, bool keep = true) => new()
    {
        DbPath = dbPath,
        DeleteDbOnDispose = !keep,
        Settings = version is null ? null : new Dictionary<string, string?> { ["SmartAdmin:Database:CodeFirstVersion"] = version },
    };

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"smart-cfv-{Guid.NewGuid():N}.db");

    private static ISqlSugarClient Db(AdminAppFactory f) => f.Services.GetRequiredService<ISqlSugarClient>();

    /// <summary>拿一个没有种子、启动期没人读写的表和它的一列当探针(sys_op_log.user_agent)。</summary>
    private static (string Table, string Column) Probe(AdminAppFactory f)
    {
        var info = Db(f).EntityMaintenance.GetEntityInfo(typeof(SysOpLog));
        return (info.DbTableName, info.Columns.First(c => c.PropertyName == nameof(SysOpLog.UserAgent)).DbColumnName);
    }

    private static bool ColumnExists(AdminAppFactory f, string table, string column) =>
        Db(f).DbMaintenance.GetColumnInfosByTableName(table, false)
            .Any(c => string.Equals(c.DbColumnName, column, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 版本未变_跳过扫描_手工删掉的列不会被补回()
    {
        var dbPath = NewDbPath();
        try
        {
            string table, column;
            using (var f = Factory(dbPath, "v1"))
            {
                _ = f.CreateClient();
                (table, column) = Probe(f);
                Db(f).Ado.ExecuteCommand($"ALTER TABLE {table} DROP COLUMN {column}");
                Assert.False(ColumnExists(f, table, column));
            }
            using (var f = Factory(dbPath, "v1"))
            {
                _ = f.CreateClient();
                Assert.False(ColumnExists(f, table, column));   // 版本没变 → 没扫 → 列仍缺
            }
            using (var f = Factory(dbPath, "v2", keep: false))
            {
                _ = f.CreateClient();
                Assert.True(ColumnExists(f, table, column));    // 改了号 → 重新扫 → 列补回
            }
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    [Fact]
    public void 版本未变但实体表缺失_照样建表()
    {
        // 加了新表忘了改号的兜底:表清单只一条 SQL,便宜;缺表就重新扫
        var dbPath = NewDbPath();
        try
        {
            string table;
            using (var f = Factory(dbPath, "v1"))
            {
                _ = f.CreateClient();
                (table, _) = Probe(f);
                Db(f).DbMaintenance.DropTable(table);
            }
            using (var f = Factory(dbPath, "v1", keep: false))
            {
                _ = f.CreateClient();
                Assert.True(Db(f).DbMaintenance.IsAnyTable(table, false));
            }
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    [Fact]
    public void 不配版本_保持每次都扫_版本行不写()
    {
        var dbPath = NewDbPath();
        try
        {
            string table, column;
            using (var f = Factory(dbPath, null))
            {
                _ = f.CreateClient();
                (table, column) = Probe(f);
                Assert.Null(Db(f).Queryable<SysSchemaVersion>().First(x => x.Id == 2));
                Db(f).Ado.ExecuteCommand($"ALTER TABLE {table} DROP COLUMN {column}");
            }
            using (var f = Factory(dbPath, null, keep: false))
            {
                _ = f.CreateClient();
                Assert.True(ColumnExists(f, table, column));   // 每次都扫 → 列补回
            }
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    [Fact]
    public void 建表日志带SQL条数与耗时()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        _ = f.CreateClient();

        var line = Assert.Single(log.Entries, e => e.Text.Contains("CodeFirst 建表完成", StringComparison.Ordinal));
        Assert.Matches(@"[1-9]\d* 条 SQL,\d+ ms", line.Text);   // 条数 > 0 证明计数钩子真挂上了
    }

    [Fact]
    public void 版本号超长_启动即抛()
    {
        using var f = Factory(NewDbPath(), new string('x', 25), keep: false);
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("CodeFirstVersion", ex.ToString());
    }
}
