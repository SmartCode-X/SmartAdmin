using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// SQL 诊断日志。若 SqlSugar 只挂 <c>Aop.DataExecuting</c>(填审计字段),而不挂
/// <c>OnError</c> / <c>OnLogExecuted</c>,线上一条查询失败就只有驱动层异常:
/// <b>没有 SQL、没有参数、没有耗时</b>,DBA 和开发都无从下手。慢查询同理:不知道慢在哪。
/// </summary>
public class SqlLoggingTests
{
/// <summary>
    /// 失败/慢 SQL 的日志会连参数值一起打——名字像口令/密钥/令牌的参数必须只出现 ***。
    /// 失败 SQL 的日志是关不掉的,慢 SQL 阈值调低时更是每条都打,值里可能有口令哈希、恢复码、业务敏感列。
    /// </summary>
    [Fact]
    public void 敏感参数值不进日志()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        Assert.ThrowsAny<Exception>(() => db.Ado.ExecuteCommand(
            "SELECT 1 FROM smart_no_such_table WHERE Password=@password AND Name=@name",
            new SugarParameter("@password", "super-secret-value"),
            new SugarParameter("@name", "张三")));

        var entry = log.Entries.Single(e => e.Level == LogLevel.Error && e.Text.Contains("smart_no_such_table", StringComparison.Ordinal));
        Assert.DoesNotContain("super-secret-value", entry.Text, StringComparison.Ordinal);
        Assert.Contains("@password=***", entry.Text, StringComparison.Ordinal);
        Assert.Contains("@name=张三", entry.Text, StringComparison.Ordinal);   // 普通参数照旧可见,否则复现不出问题
    }

    [Fact]
    public void 失败的SQL_打出语句本身()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        Assert.ThrowsAny<Exception>(() => db.Ado.ExecuteCommand("SELECT 1 FROM smart_no_such_table"));

        // 断的是"SQL 原文进了日志",不是"抛了异常"——异常本来就有,缺的正是这条线索
        Assert.Contains(log.Entries, e =>
            e.Level == LogLevel.Error && e.Text.Contains("smart_no_such_table", StringComparison.Ordinal));
    }

    [Fact]
    public void 失败的SQL_连参数一起打出来()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        Assert.ThrowsAny<Exception>(() => db.Ado.ExecuteCommand(
            "SELECT 1 FROM smart_no_such_table WHERE Id = @probe",
            new SugarParameter("@probe", 424242)));

        // 没有参数值,同一条 SQL 复现不出来 —— 只有语句是不够的
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Text.Contains("424242", StringComparison.Ordinal));
    }

    [Fact]
    public void 慢SQL_超过阈值就告警()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory
        {
            // 1ms 阈值:让任何一次真实往返都够格。跑一批语句,只要有一条被判为慢就算数
            Settings = new Dictionary<string, string?> { ["SmartAdmin:Database:SlowSqlMillis"] = "1" },
            Overrides = s => s.AddSingleton<ILoggerProvider>(log),
        };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        for (var i = 0; i < 50; i++) db.Ado.GetInt("SELECT COUNT(*) FROM sys_menu");

        Assert.Contains(log.Entries, e =>
            e.Level == LogLevel.Warning && e.Text.Contains("sys_menu", StringComparison.Ordinal));
    }

    [Fact]
    public void 慢SQL_默认阈值下不刷屏()
    {
        var log = new CaptureLoggerProvider();
        using var f = new AdminAppFactory { Overrides = s => s.AddSingleton<ILoggerProvider>(log) };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        for (var i = 0; i < 50; i++) db.Ado.GetInt("SELECT COUNT(*) FROM sys_menu");

        // 默认 1000ms:普通查询不该产生任何慢 SQL 告警,否则日志会被淹掉
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("SELECT COUNT(*) FROM sys_menu", StringComparison.Ordinal));
    }
}

/// <summary>把日志抓进内存的最小 provider(测试用)。</summary>
internal sealed class CaptureLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Text)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose() { }

    private sealed class Capture(CaptureLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => owner.Entries.Enqueue((level, formatter(state, ex) + (ex is null ? "" : " " + ex)));
    }
}
