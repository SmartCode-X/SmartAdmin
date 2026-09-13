using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 健康检查的 JSON 正文。默认写手只吐一个 <c>Healthy</c> 字面串——探针够用,人不够用:
/// <c>/health/ready</c> 变红时看不出是数据库还是缓存,更看不出慢在哪一项。
/// <para>状态码不变(Healthy=200,其余 503),所以编排层的探针配置不用动;
/// 只是正文从一个词变成一个对象。</para>
/// <para>异常只吐类型名:健康检查是<b>匿名</b>端点,连接串、主机名、库名不能从这里漏出去。</para>
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JSON = new(JsonSerializerDefaults.Web);

    /// <summary>把检查结果写成 JSON。</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                ms = (long)e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                // 只给异常类型名,不给消息:消息里常带连接串与主机名,而这是匿名端点
                error = e.Value.Exception?.GetType().Name,
                tags = e.Value.Tags,
            }),
        }, JSON));
    }
}
