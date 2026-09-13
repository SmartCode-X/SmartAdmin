using System.Security.Cryptography;
using System.Text;
using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IApiKeyValidator"/> 默认实现:按 <c>SmartAdmin:Security:ApiKey:Keys</c> 配置节比对。
/// 恒定时间比较,不因前缀相同而早退;没配任何 key 时一律不通过。
/// </summary>
public class ConfigApiKeyValidator(AdminSecurityOptions security) : IApiKeyValidator
{
    /// <inheritdoc />
    public virtual Task<ApiKeyPrincipal?> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(apiKey)) return Task.FromResult<ApiKeyPrincipal?>(null);
        var presented = Encoding.UTF8.GetBytes(apiKey);
        foreach (var entry in security.ApiKey.Keys)
        {
            if (string.IsNullOrEmpty(entry.Key) || string.IsNullOrWhiteSpace(entry.Name)) continue;
            var expected = Encoding.UTF8.GetBytes(entry.Key);
            if (expected.Length == presented.Length && CryptographicOperations.FixedTimeEquals(expected, presented))
                return Task.FromResult<ApiKeyPrincipal?>(new ApiKeyPrincipal(entry.Name.Trim(), entry.UserId));
        }
        return Task.FromResult<ApiKeyPrincipal?>(null);
    }
}
