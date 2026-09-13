using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAdmin.SqlSugar;
using SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// SQL 控制台日志(<c>Database:SqlLog</c>):开发期看 ORM 到底生成了什么、每条跑了多久。
/// <para>与 <see cref="SqlLoggingTests"/> 覆盖的慢/失败 SQL 是两件事:那两条是生产诊断且默认就在,
/// 这一套默认全关,开了才逐条打,并在请求结束补一行汇总。</para>
/// </summary>
public class SqlConsoleLogTests
{
    private static AdminAppFactory Factory(CaptureLoggerProvider log, params (string Key, string Value)[] settings) =>
        new()
        {
            Settings = settings.ToDictionary(x => x.Key, x => (string?)x.Value),
            Overrides = s => s.AddSingleton<ILoggerProvider>(log),
        };

    private static (string, string) On => ("SmartAdmin:Database:SqlLog:Enabled", "true");
    private static (string, string) NoColor => ("SmartAdmin:Database:SqlLog:Color", "false");

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>默认必须安静:开发期的观察工具漏进生产,等于每请求多打若干条日志。</summary>
    [Fact]
    public async Task 默认不打任何语句块()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        var c = await SuperAdminClient(f);

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Assert.DoesNotContain(log.Entries, e => e.Text.Contains("┌ SQL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 开启后每条语句打出SQL与耗时()
    {
        var log = new CaptureLoggerProvider();
        using var f = Factory(log, On, NoColor);
        var c = await SuperAdminClient(f);

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        // 按语句内容挑,别取 First:第一个越过阈值被打出来的块不一定是这次分页查询——SqlServer 上是建表探测的
        // IF EXISTS (SELECT …),它归到"其它"是对的,只是不是这个用例要验的那条。
        // "FROM sys_user"(四方言的引号都吃)只会命中读 sys_user 的查询,建表 / 索引探测 / INSERT 都不含它。
        var block = log.Entries.First(e =>
            e.Text.Contains("┌ SQL", StringComparison.Ordinal)
            && Regex.IsMatch(e.Text, @"FROM\s+[\[`""]?sys_user(?!\w)", RegexOptions.IgnoreCase));
        Assert.Contains("SELECT", block.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" ms", block.Text, StringComparison.Ordinal);
        Assert.Contains("查询", block.Text, StringComparison.Ordinal);
    }

    /// <summary>汇总行是查 N+1 的入口:单看语句块只会觉得"每条都很快"。</summary>
    [Fact]
    public async Task 请求结束打一行汇总()
    {
        var log = new CaptureLoggerProvider();
        using var f = Factory(log, On, NoColor);
        var c = await SuperAdminClient(f);

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        // 按路径挑:登录那个请求也会打一行汇总,而且排在前面
        var summary = log.Entries.First(e =>
            e.Text.Contains("SQL 汇总", StringComparison.Ordinal)
            && e.Text.Contains("/api/v1/sys/user/page", StringComparison.Ordinal));
        Assert.Contains("GET /api/v1/sys/user/page", summary.Text, StringComparison.Ordinal);
        Assert.Contains("条", summary.Text, StringComparison.Ordinal);
        Assert.Contains("最慢", summary.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 汇总可单独关掉()
    {
        var log = new CaptureLoggerProvider();
        using var f = Factory(log, On, NoColor, ("SmartAdmin:Database:SqlLog:RequestSummary", "false"));
        var c = await SuperAdminClient(f);

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Assert.Contains(log.Entries, e => e.Text.Contains("┌ SQL", StringComparison.Ordinal));   // 语句块照打
        Assert.DoesNotContain(log.Entries, e => e.Text.Contains("SQL 汇总", StringComparison.Ordinal));
    }

    /// <summary>阈值调到不可能达到时一条都不该打——否则 MinMillis 形同虚设。</summary>
    [Fact]
    public async Task 耗时阈值过滤掉快语句()
    {
        var log = new CaptureLoggerProvider();
        using var f = Factory(log, On, NoColor, ("SmartAdmin:Database:SqlLog:MinMillis", "100000"));
        var c = await SuperAdminClient(f);

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Assert.DoesNotContain(log.Entries, e => e.Text.Contains("┌ SQL", StringComparison.Ordinal));
    }

    [Fact]
    public void 关掉颜色后不出现转义序列()
    {
        var args = SqlLogFormatter.BlockArgs(
            1, "SELECT 1", null, 3.5, "main", maxSqlChars: 4000, color: false, slowMillis: 1000);

        Assert.DoesNotContain(args, a => a?.ToString()?.Contains('\u001b') == true);
    }

    /// <summary>参数内联进语句,但口令这类名字只出现 ***:这份日志会被贴进工单和聊天窗。</summary>
    [Fact]
    public void 参数内联且敏感值打码()
    {
        var pars = new[]
        {
            new SugarParameter("@name", "张三"),
            new SugarParameter("@password", "super-secret-value"),
        };

        var args = SqlLogFormatter.BlockArgs(
            1, "SELECT * FROM sys_user WHERE Name=@name AND Password=@password", pars,
            1.0, "main", maxSqlChars: 4000, color: false, slowMillis: 1000);
        var sql = args[4]!.ToString()!;

        Assert.Contains("'张三'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", sql, StringComparison.Ordinal);
        Assert.Contains("'***'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@name", sql, StringComparison.Ordinal);   // 真的替换掉了,不是并排打印
    }

    /// <summary>@p1 不能把 @p10 咬掉半截——按名字从长到短替换。</summary>
    [Fact]
    public void 参数名有公共前缀时不串位()
    {
        var pars = new[]
        {
            new SugarParameter("@p1", 1),
            new SugarParameter("@p10", 10),
        };

        var args = SqlLogFormatter.BlockArgs(
            1, "SELECT * FROM t WHERE a=@p1 AND b=@p10", pars,
            1.0, "main", maxSqlChars: 4000, color: false, slowMillis: 1000);

        Assert.Equal("SELECT * FROM t WHERE a=1 AND b=10", args[4]);
    }

    [Fact]
    public void 超长语句被截断()
    {
        var longSql = "SELECT " + new string('x', 500);

        var args = SqlLogFormatter.BlockArgs(
            1, longSql, null, 1.0, "main", maxSqlChars: 100, color: false, slowMillis: 1000);

        Assert.True(args[4]!.ToString()!.Length < longSql.Length);
        Assert.Contains("已截断", args[5]!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void 语句多到一定条数时汇总提示疑似NPlusOne()
    {
        var stats = new SqlLogStats("GET /x");
        for (var i = 0; i < 25; i++) stats.Record(1.0);

        var args = SqlLogFormatter.SummaryArgs(stats, color: false);

        Assert.Equal(25, args[1]);
        Assert.Contains("N+1", args[4]!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void 条数不多时不提示()
    {
        var stats = new SqlLogStats("GET /x");
        stats.Record(1.0);
        stats.Record(9.0);

        var args = SqlLogFormatter.SummaryArgs(stats, color: false);

        Assert.Equal("", args[4]);
        Assert.Contains("#2", args[3]!.ToString()!, StringComparison.Ordinal);   // 最慢的是第二条
    }
}
