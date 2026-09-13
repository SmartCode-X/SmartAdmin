using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 把当前登录用户的生效数据范围写入 <see cref="IDataScopeContext"/>(幂等:一个请求只解析一次)。
/// <para><see cref="RolePermissionAttribute"/>、<see cref="ActiveSessionAttribute"/> 与
/// <see cref="DataScopeAutoBindFilter"/> 共用。</para>
/// </summary>
internal static class DataScopeRequestBinder
{
    public static async Task BindAsync(HttpContext http, ClaimsPrincipal user, CancellationToken abort)
    {
        if (IsBound(http)) return;

        var services = http.RequestServices;
        var scopeContext = services.GetRequiredService<IDataScopeContext>();

        if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
        {
            scopeContext.Current = DataScopeResult.Unrestricted;
            return;
        }

        // 没有 sub(未绑定用户的 API Key):不绑,落到 HttpContextDataScopeContext 的 fail-closed 回退(空范围)
        if (!long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return;
        scopeContext.Current = await services.GetRequiredService<IDataScopeProvider>().ResolveAsync(userId, abort);
    }

    /// <summary>本请求是否已绑定过范围(<c>HttpContext.Items</c> 里已有值)。</summary>
    public static bool IsBound(HttpContext http) => http.Items.ContainsKey(HttpContextDataScopeContext.ITEM_KEY);
}

/// <summary>
/// 全局授权过滤器:已认证但没经过 <c>[RolePermission]</c> / <c>[ActiveSession]</c> 的请求,也把真实数据范围绑上。
/// <para>没有它的话,消费者只挂 <c>[Authorize]</c> 的端点会落到 <see cref="HttpContextDataScopeContext"/> 的
/// fail-closed 回退(空范围,查不到行)。绑定之后拿到的是用户真实范围——既不越权,也不会莫名其妙查不到数据。</para>
/// <para>幂等,故与两个授权特性叠挂时不会重复解析;匿名请求直接跳过,零开销。</para>
/// </summary>
internal sealed class DataScopeAutoBindFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return;
        await DataScopeRequestBinder.BindAsync(context.HttpContext, user, context.HttpContext.RequestAborted);
    }
}
