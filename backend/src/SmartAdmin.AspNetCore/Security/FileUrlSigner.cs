using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// <see cref="IFileUrlSigner"/> 的 HMAC-SHA256 默认实现。
/// <para>签名密钥<b>不直接用签令牌的那把</b>,而是由它派生出一把子密钥
/// (<c>HMAC(jwtKey, "smart:file-view")</c>)——两个用途各用各的密钥材料是密码学卫生:
/// 万一某处签名实现出岔子,也不会波及令牌签发。</para>
/// <para>过期时刻(若有)编进签名载荷:<c>{id}:{exp}</c>。改 URL 上的 <c>exp</c> 就验不过签名,
/// 所以延长有效期与伪造签名同等困难。</para>
/// </summary>
public class FileUrlSigner(SymmetricSecurityKey jwtKey, AdminUploadOptions? upload = null, TimeProvider? time = null)
    : IFileUrlSigner
{
    /// <summary>派生用途标签:同一把主密钥下,不同用途走不同子密钥。</summary>
    private const string PURPOSE = "smart:file-view";

    private readonly byte[] _subKey = HMACSHA256.HashData(jwtKey.Key, Encoding.UTF8.GetBytes(PURPOSE));

    private TimeProvider Clock => time ?? TimeProvider.System;

    /// <summary>配置的直链寿命;≤0 = 不过期(默认)。</summary>
    protected virtual TimeSpan? Ttl =>
        upload is { SignedUrlTtlMinutes: > 0 } o ? TimeSpan.FromMinutes(o.SignedUrlTtlMinutes) : null;

    /// <inheritdoc />
    public virtual string Sign(long fileId) => Sign(fileId, null);

    /// <inheritdoc />
    public virtual string Sign(long fileId, DateTimeOffset? expiresAt) =>
        Base64UrlEncoder.Encode(HMACSHA256.HashData(_subKey, Encoding.UTF8.GetBytes(Payload(fileId, expiresAt))));

    /// <inheritdoc />
    public virtual bool Verify(long fileId, string? signature) => Verify(fileId, signature, null);

    /// <inheritdoc />
    public virtual bool Verify(long fileId, string? signature, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        // 配了 TTL,无期限的老链接就一律拒绝——否则等于给了一条绕过有效期的路
        if (Ttl is not null && expiresAt is null) return false;
        if (expiresAt is { } exp && Clock.GetUtcNow() > exp) return false;

        byte[] provided;
        try { provided = Base64UrlEncoder.DecodeBytes(signature); }
        catch { return false; }   // 畸形签名(非 base64url)= 校验失败,不是 500

        var expected = HMACSHA256.HashData(_subKey, Encoding.UTF8.GetBytes(Payload(fileId, expiresAt)));
        return CryptographicOperations.FixedTimeEquals(expected, provided);   // 定长比较:不给按前缀爆破留时间侧信道
    }

    /// <inheritdoc />
    public virtual string BuildUrl(long fileId)
    {
        if (Ttl is not { } ttl) return $"/api/v1/sys/file/{fileId}/view?sig={Sign(fileId, null)}";

        var exp = Clock.GetUtcNow().Add(ttl);
        return $"/api/v1/sys/file/{fileId}/view?sig={Sign(fileId, exp)}&exp={exp.ToUnixTimeSeconds()}";
    }

    /// <summary>签名载荷:无期限是 <c>{id}</c>,带期限是 <c>{id}:{unix 秒}</c>。</summary>
    private static string Payload(long fileId, DateTimeOffset? expiresAt) =>
        expiresAt is { } exp
            ? string.Create(CultureInfo.InvariantCulture, $"{fileId}:{exp.ToUnixTimeSeconds()}")
            : fileId.ToString(CultureInfo.InvariantCulture);
}
