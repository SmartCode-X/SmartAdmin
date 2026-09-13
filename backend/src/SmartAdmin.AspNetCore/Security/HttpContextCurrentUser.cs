using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// <see cref="ICurrentUser"/> 默认实现:从当前请求的 JWT claim 读取(不查库)。
/// sub → 用户 Id;sadm → 超管标志(与授权管道判定一致)。
/// </summary>
public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>当前请求的认证主体;非 HTTP 场景(后台任务/启动)为 <c>null</c>。</summary>
    protected ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public virtual bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public virtual long? UserId =>
        long.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    /// <inheritdoc />
    public virtual string? SessionId
    {
        get
        {
            var sid = Principal?.FindFirstValue(TokenClaimNames.SESSION_ID);
            return string.IsNullOrEmpty(sid) ? null : sid;
        }
    }

    /// <inheritdoc />
    public virtual bool IsSuperAdmin => Principal?.HasClaim(TokenClaimNames.SUPER_ADMIN, "true") == true;

    /// <inheritdoc />
    public virtual long? OrgId =>
        long.TryParse(Principal?.FindFirstValue(TokenClaimNames.ORG_ID), out var orgId) ? orgId : null;

    /// <inheritdoc />
    // ponytail: 直接取 TCP 连接对端 IP。反向代理后面拿到的是代理 IP——上正式网关时按需接
    //           ForwardedHeaders 中间件解析 X-Forwarded-For,这里不预埋。
    public virtual string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public virtual string? UserAgent
    {
        // Headers.UserAgent 是强类型访问器(StringValues);空串归一为 null,日志里少一列噪声
        get
        {
            var ua = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(ua) ? null : ua;
        }
    }
}
