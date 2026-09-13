namespace SmartAdmin.Core;

/// <summary>
/// 一把 API Key 校验通过后的身份。<paramref name="Name"/> 进日志与限流分区;
/// <paramref name="UserId"/> 可选——绑了用户,这把 key 就以该用户的角色与数据范围调 <c>[RolePermission]</c> 端点,
/// 不绑则只能调仅挂 <c>[ApiKey]</c> 的端点,数据范围按空范围(fail-closed)处理。
/// </summary>
public sealed record ApiKeyPrincipal(string Name, long? UserId = null);

/// <summary>
/// API Key 校验(机器端接入:设备 / 第三方系统没有登录会话,拿一把预共享密钥调接口)。
/// 内核默认实现读 <c>SmartAdmin:Security:ApiKey:Keys</c> 配置节;要从数据库表、密钥管理服务取,
/// 在 <c>AddSmartAdmin()</c> 之前注册自己的实现即整体替换(TryAdd)。
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>校验一把 key;不认识或已停用返回 null。key 已去首尾空白。</summary>
    Task<ApiKeyPrincipal?> ValidateAsync(string apiKey, CancellationToken cancellationToken = default);
}
