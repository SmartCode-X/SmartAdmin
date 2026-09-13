using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 给每个请求开一个 <see cref="SqlLogScope"/>,结束时打一行汇总:条数、合计耗时、最慢的是第几条。
/// <para>单看语句块只会觉得"每条都很快",汇总里"一个请求 143 条"才刺眼——查 N+1 靠的是这一行。</para>
/// <para>仅在 <c>Database:SqlLog:Enabled</c> 且 <c>RequestSummary</c> 都开时才注册,不占默认管道。</para>
/// </summary>
internal sealed class SqlLogRequestMiddleware(
    RequestDelegate next,
    AdminDatabaseOptions db,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("SmartAdmin.Sql");
    private readonly bool _color = SqlLogFormatter.ShouldColor(db.SqlLog.Color);

    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = SqlLogScope.Begin($"{context.Request.Method} {context.Request.Path}");
        try
        {
            await next(context);
        }
        finally
        {
            // 一条 SQL 都没跑的请求(静态资源、404)不值得占一行
            if (SqlLogScope.Current is { Count: > 0 } stats)
                _logger.LogInformation(SqlLogFormatter.SUMMARY_TEMPLATE, SqlLogFormatter.SummaryArgs(stats, _color));
        }
    }
}
