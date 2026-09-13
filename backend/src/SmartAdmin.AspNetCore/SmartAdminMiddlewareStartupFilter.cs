using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 中间件挂载点:零配置宿主的 <c>MapSmartAdmin</c> 只拿到 <c>IEndpointRouteBuilder</c>、
/// 无处插中间件。用 <see cref="IStartupFilter"/> 在管道前段注入需要中间件的横切能力(转发头 + CORS + 限流),
/// 用户仍只调 <c>AddSmartAdmin</c> / <c>MapSmartAdmin</c>、无需手动 <c>UseForwardedHeaders</c>/<c>UseCors</c>/<c>UseRateLimiter</c>。
/// <para>次序有硬约束:
/// <b>①转发头</b>必须<b>最先</b>——它重写 <c>Connection.RemoteIpAddress</c>,晚一步下游(限流分区、日志 IP)
/// 读到的就还是代理 IP;
/// <b>②CORS</b>(预检 OPTIONS 先于一切放行);
/// <b>③限流</b>(尽早挡洪泛,省下游开销,且此时 IP 已是真实客户端)。</para>
/// <para>开了 SQL 控制台日志时末尾再加一层统计范围(<see cref="SqlLogRequestMiddleware"/>),它要盖住业务的全部语句。</para>
/// <para>后两者应用的都是<b>全局策略</b>(CORS 命名默认策略 / 限流全局分区器按 <c>Request.Path</c> 区分认证端点),
/// 不依赖端点元数据,故置于 UseRouting 之前也正确。认证/授权中间件由 WebApplication 在其后自动插入,次序不冲突。</para>
/// </summary>
internal sealed class SmartAdminMiddlewareStartupFilter(
    AdminApiOptions api,
    AdminDatabaseOptions database) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        // 默认关:不在代理后面时启用它 = 允许任何人经 X-Forwarded-For 伪造自己的 IP。
        // 受信来源在 AddSmartAdmin 里绑进 ForwardedHeadersOptions(未声明受信来源时那里已 fail-fast)。
        if (api.ForwardedHeaders.Enabled) app.UseForwardedHeaders();
        app.UseCors(SmartAdminSetup.CorsPolicyName);
        app.UseMiddleware<RateLimitMiddleware>();
        // 双提交 CSRF:限流之后、认证之前即可(只看 Cookie/头);非 Cookie 会话时中间件内直通
        app.UseMiddleware<CsrfMiddleware>();
        // SQL 汇总:放在最里层,统计范围要盖住业务执行的每一条语句。默认不注册,开了才有。
        if (database.SqlLog.Enabled && database.SqlLog.RequestSummary)
            app.UseMiddleware<SqlLogRequestMiddleware>();
        next(app);
    };
}
