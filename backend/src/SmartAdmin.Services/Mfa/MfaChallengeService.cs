using Microsoft.Extensions.Logging;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IMfaChallengeService"/> 默认实现:挑战存缓存,校验时解密用户 TOTP seed 并 <see cref="ITotpService.Verify"/>。
/// </summary>
public class MfaChallengeService(
    ICacheProvider cache,
    IRepository<SysUser> users,
    ITotpService totp,
    ISecretProtector protector,
    AdminSecurityOptions security,
    ILogger<MfaChallengeService>? logger = null) : IMfaChallengeService
{
    /// <inheritdoc />
    public virtual async Task<string> CreateChallengeAsync(long userId)
    {
        var challengeId = Guid.CreateVersion7().ToString("N");
        // Totp:ChallengeTtlSeconds,未配回退 300 秒(见 ResolveTotpChallengeTtlSeconds)
        var ttl = TimeSpan.FromSeconds(Math.Max(60, security.ResolveTotpChallengeTtlSeconds()));
        await cache.SetAsync(CacheKeys.TotpMfaChallenge(challengeId), userId, ttl);
        return challengeId;
    }

    /// <inheritdoc />
    public virtual Task<long> GetChallengeAsync(string challengeId) =>
        string.IsNullOrWhiteSpace(challengeId)
            ? Task.FromResult(0L)
            : cache.GetAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));

    /// <inheritdoc />
    public virtual async Task<long> VerifyAndConsumeAsync(string challengeId, string totpCode)
    {
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(challengeId), ErrorCode.TotpWrong);
        AdminException.ThrowIf(string.IsNullOrWhiteSpace(totpCode), ErrorCode.TotpWrong);

        var userId = await cache.GetAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));
        AdminException.ThrowIf(userId == 0, ErrorCode.TotpWrong);

        var user = await users.GetByIdAsync(userId);
        AdminException.ThrowIf(user is null || !user.TotpEnabled || string.IsNullOrEmpty(user.TotpSeedProtected),
            ErrorCode.TotpNotBound);

        string seed;
        // 解密失败不是"码输错了":多半是数据保护密钥换了/配丢了,或密文被改。对外仍归一到同一个码(不给探测面),
        // 但必须留痕——否则运维看到的只有满屏"验证码错误",查不到真正的原因。
        try { seed = protector.Unprotect(user.TotpSeedProtected!); }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "TOTP 种子解密失败(userId={UserId}),按口令错误返回", userId);
            throw new AdminException(ErrorCode.TotpWrong);
        }

        if (!totp.Verify(seed, totpCode.Trim()))
            throw new AdminException(ErrorCode.TotpWrong);

        // 码对才消费挑战(防并发重放)
        var consumed = await cache.GetAndRemoveAsync<long>(CacheKeys.TotpMfaChallenge(challengeId));
        AdminException.ThrowIf(consumed == 0, ErrorCode.TotpWrong);
        return consumed;
    }
}
