using Microsoft.IdentityModel.Tokens;
using System.Text;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 签名直链的可选寿命。默认不过期(链接会被存进公告正文这类持久内容,给寿命等于让它们集体坏掉);
/// 配了 <c>Upload:SignedUrlTtlMinutes</c> 之后过期时刻编进签名,改 URL 上的 exp 就验不过。
/// </summary>
public class SignedUrlExpiryTests
{
    private static readonly SymmetricSecurityKey KEY =
        new(Encoding.UTF8.GetBytes("smart-signed-url-test-key-please-keep-32plus"));

    private static (FileUrlSigner Signer, FakeTimeProvider Clock) Make(int ttlMinutes)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-06T10:00:00Z"));
        return (new FileUrlSigner(KEY, new AdminUploadOptions { SignedUrlTtlMinutes = ttlMinutes }, clock), clock);
    }

    [Fact]
    public void Default_url_has_no_expiry_and_verifies()
    {
        var (signer, _) = Make(0);

        var url = signer.BuildUrl(42);

        Assert.DoesNotContain("exp=", url);
        Assert.True(signer.Verify(42, signer.Sign(42), null));
    }

    [Fact]
    public void Configured_ttl_puts_exp_in_the_url_and_it_verifies()
    {
        var (signer, clock) = Make(30);

        var url = signer.BuildUrl(42);
        var (sig, exp) = Parse(url);

        Assert.True(signer.Verify(42, sig, exp));
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(signer.Verify(42, sig, exp));   // 过期即拒
    }

    /// <summary>延长有效期与伪造签名同等困难:exp 是签名载荷的一部分。</summary>
    [Fact]
    public void Tampering_with_exp_breaks_the_signature()
    {
        var (signer, _) = Make(30);
        var (sig, exp) = Parse(signer.BuildUrl(42));

        Assert.False(signer.Verify(42, sig, exp!.Value.AddYears(1)));
    }

    /// <summary>配了 TTL 之后,无期限的旧链接一律拒绝——否则等于留了一条绕过有效期的路。</summary>
    [Fact]
    public void Legacy_unexpiring_link_is_rejected_once_ttl_is_configured()
    {
        var (noTtl, _) = Make(0);
        var legacy = noTtl.Sign(42, null);

        var (withTtl, _) = Make(30);
        Assert.False(withTtl.Verify(42, legacy, null));
    }

    [Fact]
    public void Signature_of_another_file_does_not_verify()
    {
        var (signer, _) = Make(0);
        Assert.False(signer.Verify(43, signer.Sign(42), null));
    }

    private static (string Sig, DateTimeOffset? Exp) Parse(string url)
    {
        var query = url[(url.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);
        var exp = query.TryGetValue("exp", out var raw)
            ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(raw))
            : (DateTimeOffset?)null;
        return (query["sig"], exp);
    }
}
