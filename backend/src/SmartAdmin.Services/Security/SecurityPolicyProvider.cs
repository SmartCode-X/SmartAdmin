using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="ISecurityPolicyProvider"/> 默认实现:每个值先读 <see cref="SysConfig"/>
/// (<see cref="IConfigService.GetValueByKeyAsync"/>——读穿透缓存、改动即失效),
/// 缺失或解析失败则回退到 Options 默认。配置键常量集中在此,<see cref="ConfigSeed"/> 与前端安全策略 Tab 均以此对齐。
/// </summary>
public class SecurityPolicyProvider(
    IConfigService config,
    AdminSecurityOptions security,
    AdminJwtOptions jwt) : ISecurityPolicyProvider
{
    /// <summary>安全策略配置项分组编码(配置中心「安全策略」Tab 按此分组加载)</summary>
    public const string GROUP = "security";

    /// <summary>配置键:登录失败锁定阈值(连续失败几次触发锁定)。</summary>
    public const string KEY_MAX_FAIL = "sys.security.loginLock.maxFailCount";

    /// <summary>配置键:登录失败锁定时长(分钟)。</summary>
    public const string KEY_LOCK_MIN = "sys.security.loginLock.lockMinutes";

    /// <summary>配置键:密码最小长度。</summary>
    public const string KEY_MIN_LEN = "sys.security.password.minLength";

    /// <summary>配置键:密码是否要求含大写字母。</summary>
    public const string KEY_REQ_UPPER = "sys.security.password.requireUpper";

    /// <summary>配置键:密码是否要求含小写字母。</summary>
    public const string KEY_REQ_LOWER = "sys.security.password.requireLower";

    /// <summary>配置键:密码是否要求含数字。</summary>
    public const string KEY_REQ_DIGIT = "sys.security.password.requireDigit";

    /// <summary>配置键:密码是否要求含特殊字符。</summary>
    public const string KEY_REQ_SPECIAL = "sys.security.password.requireSpecial";

    /// <summary>配置键:密码过期天数(0=不过期)。</summary>
    public const string KEY_EXPIRE_DAYS = "sys.security.password.expireDays";

    /// <summary>配置键:密码历史防重用条数(0=关)。</summary>
    public const string KEY_HISTORY_COUNT = "sys.security.password.historyCount";

    /// <summary>配置键:访问令牌有效期(分钟)。</summary>
    public const string KEY_ACCESS_MIN = "sys.security.session.accessMinutes";

    /// <summary>配置键:刷新令牌有效期(分钟)。</summary>
    public const string KEY_REFRESH_MIN = "sys.security.session.refreshMinutes";

    /// <summary>密码最小长度的默认值(配置缺失时的兜底,须与 <see cref="ConfigSeed"/> 播种默认一致)。</summary>
    public const int DEFAULT_MIN_LEN = 8;

    /// <inheritdoc />
    public virtual async Task<(int MaxFailCount, int LockMinutes)> GetLoginLockAsync() =>
        (await IntAsync(KEY_MAX_FAIL, security.LoginLock.MaxFailCount),
         await IntAsync(KEY_LOCK_MIN, security.LoginLock.LockMinutes));

    /// <inheritdoc />
    public virtual async Task<(int AccessMinutes, int RefreshMinutes)> GetSessionTtlAsync() =>
        (await IntAsync(KEY_ACCESS_MIN, jwt.ExpireMinutes),
         await IntAsync(KEY_REFRESH_MIN, jwt.RefreshExpireMinutes));

    /// <inheritdoc />
    public virtual async Task<PasswordPolicy> GetPasswordPolicyAsync()
    {
        var minLen = await IntAsync(KEY_MIN_LEN, DEFAULT_MIN_LEN);
        var reqUpper = await BoolAsync(KEY_REQ_UPPER, true);
        var reqLower = await BoolAsync(KEY_REQ_LOWER, true);
        var reqDigit = await BoolAsync(KEY_REQ_DIGIT, true);
        var reqSpecial = await BoolAsync(KEY_REQ_SPECIAL, false);

        return new PasswordPolicy(minLen, reqUpper, reqLower, reqDigit, reqSpecial);
    }

    /// <inheritdoc />
    public virtual Task<int> GetPasswordExpireDaysAsync() => IntAsync(KEY_EXPIRE_DAYS, 0);

    /// <inheritdoc />
    public virtual Task<int> GetPasswordHistoryCountAsync() => IntAsync(KEY_HISTORY_COUNT, 0);

    /// <inheritdoc />
    public virtual async Task ValidatePasswordAsync(string password)
    {
        var p = await GetPasswordPolicyAsync();
        var pw = password ?? "";
        var hasUpper = pw.Any(char.IsUpper);
        var hasLower = pw.Any(char.IsLower);
        var hasDigit = pw.Any(char.IsDigit);
        var hasSpecial = pw.Any(c => !char.IsLetterOrDigit(c));

        var ok = pw.Length >= p.MinLength
            && (!p.RequireUpper || hasUpper)
            && (!p.RequireLower || hasLower)
            && (!p.RequireDigit || hasDigit)
            && (!p.RequireSpecial || hasSpecial);

        AdminException.ThrowIf(!ok, ErrorCode.PasswordTooWeak, new Dictionary<string, object?>
        {
            ["minLength"] = p.MinLength,
            ["requireUpper"] = p.RequireUpper,
            ["requireLower"] = p.RequireLower,
            ["requireDigit"] = p.RequireDigit,
            ["requireSpecial"] = p.RequireSpecial,
        });
    }

    // DB 值优先、解析失败回退默认。ponytail: 逐键读足够——键少且 IConfigService 已读穿透缓存,不预造批量读接口。
    private async Task<int> IntAsync(string key, int fallback) =>
        int.TryParse(await config.GetValueByKeyAsync(key), out var v) ? v : fallback;

    private async Task<bool> BoolAsync(string key, bool fallback) =>
        bool.TryParse(await config.GetValueByKeyAsync(key), out var v) ? v : fallback;
}
