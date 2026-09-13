using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 接口授权标记:<b>无参数、无权限字符串</b>——权限码就是规范化路由。
/// <para>授权管道:未认证 → 401;超管(令牌 sadm claim)→ 放行;
/// 其余取 <see cref="IPermissionProvider"/> 的权限码集合,包含
/// <c>{METHOD}:/{路由模板}</c>(如 <c>GET:/api/v1/ping</c>)才放行,否则 403 + 41001。</para>
/// <para>用户业务接口同样只需挂 <c>[RolePermission]</c>,角色-菜单授权界面上勾选路由即完成配权,
/// 代码里永远不出现 <c>"sys:user:add"</c> 之类的魔法字符串。</para>
/// <para>每一步都是 <c>protected virtual</c>:要加"带某 claim 即放行""按租户改权限码形状"这类规则,
/// 继承本特性覆写对应一步即可,不必复制整个管道。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RolePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <inheritdoc />
    public virtual async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // 没有监听者时 StartActivity 返回 null,这一整段的开销就是一次判空
        using var activity = SmartAdminDiagnostics.StartActivity("smartadmin.authorize");

        // 1. 必须已通过 JWT 认证(令牌缺失/过期/被篡改在认证中间件即被拒)。
        //    401 也套统一信封(40006),与会话失活/框架 challenge 出口一致,前端可读 msgKey。
        if (!IsAuthenticated(user))
        {
            Record(activity, "unauthenticated");
            context.Result = Unauthorized();
            return;
        }

        // 2. 会话状态校验(强退即时生效):sid 对应会话被吊销/过期 → 401,超管同样受此约束。
        if (!await IsSessionActiveAsync(context))
        {
            Record(activity, "session-dead");
            context.Result = Unauthorized();
            return;
        }

        // 数据范围载体:本请求后续的 DataEntity 查询由全局过滤器读它。
        // 在授权阶段(动作执行前)写入,保证查询时已就绪。与 [ActiveSession] 同源,见 DataScopeRequestBinder。
        await BindDataScopeAsync(context);

        // 3. 超管直接放行(claim 随令牌下发,零查库;授权管道第一步判定)。范围已在 Bind 里写成 Unrestricted。
        if (IsSuperAdmin(user))
        {
            Record(activity, "allow");
            return;
        }

        // 4. 权限码 = 规范化路由(含 HTTP Method),与用户权限码集合比对
        var code = BuildPermissionCode(context);
        activity?.SetTag("smartadmin.permission_code", code);
        if (!await HasPermissionAsync(context, code))
        {
            Record(activity, "deny");
            context.Result = Forbidden();
            return;
        }
        Record(activity, "allow");
    }

    /// <summary>把判定结果同时记进 span 标签与计数器。</summary>
    private static void Record(System.Diagnostics.Activity? activity, string result)
    {
        activity?.SetTag("smartadmin.result", result);
        SmartAdminDiagnostics.Authorizations.Add(1, new KeyValuePair<string, object?>("result", result));
    }

    /// <summary>已通过认证(JWT 签名与有效期已在认证中间件校验)。</summary>
    protected virtual bool IsAuthenticated(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true;

    /// <summary>超管标记来自令牌 claim,零查库。</summary>
    protected virtual bool IsSuperAdmin(ClaimsPrincipal user) => user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true");

    /// <summary>经 API Key 认证的机器主体:没有会话,跳过会话校验;权限仍按它绑定的用户判。</summary>
    protected virtual bool IsApiKeyPrincipal(ClaimsPrincipal user) => user.HasClaim(c => c.Type == TokenClaimNames.API_KEY);

    /// <summary>会话仍活跃(强退/登出后即时失效)。机器主体没有会话,直接视为活跃。</summary>
    protected virtual async Task<bool> IsSessionActiveAsync(AuthorizationFilterContext context)
    {
        if (IsApiKeyPrincipal(context.HttpContext.User)) return true;
        var sessionId = context.HttpContext.User.FindFirstValue(TokenClaimNames.SESSION_ID);
        if (string.IsNullOrEmpty(sessionId)) return false;
        return await context.HttpContext.RequestServices.GetRequiredService<ISessionService>().IsActiveAsync(sessionId);
    }

    /// <summary>解析并写入本请求的数据范围。</summary>
    protected virtual Task BindDataScopeAsync(AuthorizationFilterContext context) =>
        DataScopeRequestBinder.BindAsync(context.HttpContext, context.HttpContext.User, context.HttpContext.RequestAborted);

    /// <summary>本请求对应的权限码(规范化规则见 <see cref="PermissionCode"/>——授权、路由清单、日志三处共用同一真源)。</summary>
    protected virtual string BuildPermissionCode(AuthorizationFilterContext context) =>
        PermissionCode.Build(
            context.HttpContext.Request.Method,
            context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.HttpContext.Request.Path.Value);

    /// <summary>当前用户是否持有该权限码。没有 sub(未绑定用户的 API Key)= 无任何权限。</summary>
    protected virtual async Task<bool> HasPermissionAsync(AuthorizationFilterContext context, string code)
    {
        if (!long.TryParse(context.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return false;
        var codes = await context.HttpContext.RequestServices.GetRequiredService<IPermissionProvider>()
            .GetPermissionCodesAsync(userId, context.HttpContext.RequestAborted);
        return codes.Contains(code);
    }

    /// <summary>401 出口(统一信封 40006)。</summary>
    protected virtual IActionResult Unauthorized() =>
        new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid)) { StatusCode = StatusCodes.Status401Unauthorized };

    /// <summary>403 出口(统一信封 41001)。</summary>
    protected virtual IActionResult Forbidden() =>
        new ObjectResult(Result<object>.Fail(ErrorCode.NoPermission)) { StatusCode = StatusCodes.Status403Forbidden };
}
