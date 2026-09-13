using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// HTTP 环境下的 <see cref="IDataScopeContext"/> 实现:用 <c>HttpContext.Items</c> 存当前请求的生效范围。
/// <para>为什么不用 AsyncLocal:授权过滤器是 MVC 管道的被调用方,其内部 <c>await</c> 之后设置的 AsyncLocal
/// 不会回流到管道上游(经典陷阱),动作里的查询将读不到。<c>HttpContext.Items</c> 挂在请求对象上、
/// 全管道稳定可见,无此问题。非 HTTP 场景(自检/后台)回退到 SqlSugar 层的 AsyncLocal 实现。</para>
/// <para><b>未绑定时的回退是 fail-closed 的</b>:认证请求拿到空范围(查不到任何受控行),而不是"看全库"。
/// 正常情况下 <see cref="DataScopeAutoBindFilter"/> 已在授权阶段绑好真实范围,回退只在异常路径上兜底。
/// 无 HttpContext(后台任务/启动)、匿名请求、超管仍然不受限。要让回退改为不受限,配
/// <c>SmartAdmin:Security:DataScopeFailOpen=true</c>。</para>
/// </summary>
public class HttpContextDataScopeContext(IHttpContextAccessor accessor, AdminSecurityOptions? security = null)
    : IDataScopeContext
{
    internal static readonly object ITEM_KEY = new();

    /// <inheritdoc />
    public virtual DataScopeResult Current
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null) return DataScopeResult.Unrestricted;   // 非 HTTP:后台任务/启动/种子,可信上下文
            return ctx.Items.TryGetValue(ITEM_KEY, out var v) && v is DataScopeResult r ? r : Fallback(ctx);
        }
        set
        {
            var ctx = accessor.HttpContext;
            if (ctx is not null) ctx.Items[ITEM_KEY] = value;
        }
    }

    /// <summary>
    /// 未绑定范围时的回退。匿名请求与超管不受限;其余认证请求按空范围处理(仅本人)。
    /// </summary>
    protected virtual DataScopeResult Fallback(HttpContext ctx)
    {
        var user = ctx.User;
        if (user.Identity?.IsAuthenticated != true) return DataScopeResult.Unrestricted;
        if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true")) return DataScopeResult.Unrestricted;
        if (security?.DataScopeFailOpen == true) return DataScopeResult.Unrestricted;

        _ = long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId);
        return DataScopeResult.Restricted([], includeSelf: true, userId);
    }
}
