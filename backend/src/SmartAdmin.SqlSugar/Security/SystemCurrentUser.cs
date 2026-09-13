using SmartAdmin.Core;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// <see cref="ICurrentUser"/> 的兜底实现:恒"未认证/系统上下文"。
/// HTTP 环境由 AspNetCore 层的 <c>HttpContextCurrentUser</c> 前置注册覆盖;
/// 非 HTTP(启动、种子、自检、后台)用它——审计字段填充遇它则留空(系统写入不归属某用户)。
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    /// <inheritdoc/>
    public bool IsAuthenticated => false;
    /// <inheritdoc/>
    public long? UserId => null;
    /// <inheritdoc/>
    public string? SessionId => null;
    /// <inheritdoc/>
    public bool IsSuperAdmin => false;
    /// <inheritdoc/>
    public long? OrgId => null;

    // 非 HTTP 上下文(启动/种子/后台)没有请求来源,IP/UA 恒 null
    /// <inheritdoc/>
    public string? IpAddress => null;
    /// <inheritdoc/>
    public string? UserAgent => null;
}
