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
/// 高风险操作短时再次认证校验(约 5 分钟窗口)。
/// 按 JWT <c>sid</c> + userId 调用 <see cref="IReauthService.IsGrantedAsync"/>;
/// 未授予 → 403 + <see cref="ErrorCode.ReauthRequired"/>。
/// 不在 AuthService 内硬编码路由列表——由控制器/动作显式挂本特性。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireReauthAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <inheritdoc />
    public virtual async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 仅 TOTP 能力开启时强制再认证(SysConfig / Options)
        var policy = context.HttpContext.RequestServices.GetService<IMfaPolicyService>();
        if (policy is null || !await policy.IsTotpFeatureEnabledAsync())
            return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid))
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub) || !long.TryParse(sub, out var userId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.TokenInvalid))
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
            return;
        }

        var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID)
                        ?? user.FindFirstValue("sid");

        if (!await IsGrantedAsync(context, userId, sessionId))
        {
            context.Result = new ObjectResult(Result<object>.Fail(ErrorCode.ReauthRequired))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }

    /// <summary>窗口内是否已完成再次认证。覆写点:换成自己的再认证凭据(如硬件密钥)。</summary>
    protected virtual Task<bool> IsGrantedAsync(AuthorizationFilterContext context, long userId, string? sessionId) =>
        context.HttpContext.RequestServices.GetRequiredService<IReauthService>().IsGrantedAsync(userId, sessionId: sessionId);
}
