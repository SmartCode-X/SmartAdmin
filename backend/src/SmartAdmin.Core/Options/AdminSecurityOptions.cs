namespace SmartAdmin.Core;

/// <summary>会话并发模式(对应 <c>SmartAdmin:Security:Session:Mode</c>)。</summary>
public enum SessionMode
{
    /// <summary>多端并存(默认):同一用户可有多个活跃会话</summary>
    Multi,

    /// <summary>单端:新登录吊销该用户其他所有会话(挤下线)</summary>
    Single,
}

/// <summary>
/// 数据保护密钥(对应 <c>SmartAdmin:Security:DataProtection</c>)。
/// 为 <c>ISecretProtector</c> 提供主密钥材料;可替换为 KMS 的 <c>IDataProtectionKeyProvider</c>。
/// </summary>
public class AdminDataProtectionOptions
{
    /// <summary>
    /// 主密钥 Base64(解码后建议 ≥32 字节)。null 时开发环境可自动生成落盘密钥;
    /// 生产启用 TOTP/Cookie 等涉密能力时建议显式配置。
    /// </summary>
    public string? Key { get; set; }

    /// <summary>当前密钥版本号(轮换时递增;信封带版本以便渐进解密)</summary>
    public int KeyVersion { get; set; } = 1;
}

/// <summary>
/// TOTP 二因子(对应 <c>SmartAdmin:Security:Totp</c>)。默认全关。
/// <para>运行时总闸与登录验证码同款:SysConfig <see cref="KEY_ENABLED"/> 可在配置中心即时开关;
/// <see cref="Enabled"/> 为部署级地板(Options 为真则始终开;默认 false,以 UI/DB 为准)。</para>
/// 绑定模型:用户自助(ADR 0006);恢复码在绑定时下发。
/// </summary>
public class AdminTotpOptions
{
    /// <summary>SysConfig 键:启用 TOTP 能力(绑定/登录挑战/恢复码)。改值即时生效。</summary>
    public const string KEY_ENABLED = "sys.security.totp.enabled";

    /// <summary>SysConfig 键:超管是否必须第二因子。改值即时生效。</summary>
    public const string KEY_REQUIRE_FOR_SUPER_ADMIN = "sys.security.totp.requireForSuperAdmin";

    /// <summary>
    /// 部署级是否启用 TOTP(appsettings)。默认 <c>false</c>。
    /// 与 <see cref="KEY_ENABLED"/> 任一为真即开能力;生产若写 true 则 UI 无法关断(硬开地板)。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 启用 TOTP 时,超级管理员是否必须完成第二因子(未绑定则登录引导自助绑定)。
    /// 默认 <c>false</c>;运行时可由 <see cref="KEY_REQUIRE_FOR_SUPER_ADMIN"/> 覆盖。
    /// </summary>
    public bool RequireForSuperAdmin { get; set; }

    /// <summary>otpauth URI 中的 issuer(Authenticator 展示名);默认 SmartAdmin。</summary>
    public string Issuer { get; set; } = "SmartAdmin";

    /// <summary>登录/提权 TOTP 挑战有效期(秒);默认 300。</summary>
    public int ChallengeTtlSeconds { get; set; } = 300;

    /// <summary>高危操作再次确认有效窗口(分钟);默认 5。</summary>
    public int ReauthWindowMinutes { get; set; } = 5;

    /// <summary>每次绑定生成的恢复码个数;默认 10。</summary>
    public int RecoveryCodeCount { get; set; } = 10;
}

/// <summary>会话配置(对应 <c>SmartAdmin:Security:Session</c>)。</summary>
public class AdminSessionOptions
{
    /// <summary>
    /// 是否使用 Cookie 会话模式:refresh → HttpOnly Cookie,access → 仅内存,并启用双提交 CSRF。
    /// 默认 <c>false</c> = body 模式:刷新令牌走 JSON 体,前端存在 localStorage。
    /// 配置键:<c>SmartAdmin:Security:Session:CookieMode</c>。
    /// </summary>
    public bool CookieMode { get; set; }

    /// <summary>
    /// Cookie Domain(如 <c>.example.com</c>)。空 = 当前 host(推荐同源反代)。
    /// 跨源 SPA+API 时须显式设置,并配合 CORS 凭证。
    /// 配置键:<c>SmartAdmin:Security:Session:CookieDomain</c>。
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>并发模式:Multi(默认)| Single(新登录踢旧)</summary>
    public SessionMode Mode { get; set; } = SessionMode.Multi;

    /// <summary>最大并发会话数;&gt;0 时超出则吊销最旧。0 = 不限(默认)。</summary>
    public int MaxConcurrent { get; set; }

    /// <summary>
    /// 会话活动回写节流(秒):热路径更新缓存,满间隔再写 DB。默认 60。
    /// 仅当启用闲置超时(<see cref="IdleMinutesNormal"/> &gt; 0)时有意义。
    /// </summary>
    public int ActivityThrottleSeconds { get; set; } = 60;

    /// <summary>
    /// 普通用户闲置超时(分钟)。<b>0 = 不启用闲置过期</b>(默认,零配置不杀会话)。
    /// 配置键:<c>SmartAdmin:Security:Session:IdleMinutesNormal</c>。
    /// </summary>
    public int IdleMinutesNormal { get; set; }

    /// <summary>
    /// 已启用 TOTP 的用户闲置超时(分钟)。0 = 与 <see cref="IdleMinutesNormal"/> 相同。
    /// 配置键:<c>SmartAdmin:Security:Session:IdleMinutesMfa</c>。
    /// </summary>
    public int IdleMinutesMfa { get; set; }

    /// <summary>
    /// 绝对会话最长寿命(小时)。0 = 不额外限制(默认,仅随 refresh 过期)。
    /// 配置键:<c>SmartAdmin:Security:Session:AbsoluteHours</c>。
    /// </summary>
    public int AbsoluteHours { get; set; }
}

/// <summary>验证码配置(对应 <c>SmartAdmin:Security:Captcha</c>)。</summary>
public class AdminCaptchaOptions
{
    /// <summary>是否启用登录验证码。默认关。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 验证码类型:<c>char</c>(默认)| <c>path</c>| <c>math</c>。
    /// 运行时可以 DB 键 <c>sys.security.captcha.type</c> 覆盖。
    /// </summary>
    public string Type { get; set; } = "char";
}

/// <summary>登录失败锁定(对应 <c>SmartAdmin:Security:LoginLock</c>)。</summary>
public class AdminLoginLockOptions
{
    /// <summary>连续密码错误多少次后锁定,默认 5;&lt;=0 表示关闭。</summary>
    public int MaxFailCount { get; set; } = 5;

    /// <summary>锁定时长(分钟),默认 10;也是失败计数滑动窗口。</summary>
    public int LockMinutes { get; set; } = 10;
}

/// <summary>请求限流(对应 <c>SmartAdmin:Security:RateLimit</c>)。</summary>
public class AdminRateLimitOptions
{
    /// <summary>SysConfig 键:限流总开关。改值即时生效。</summary>
    public const string KEY_ENABLED = "sys.security.rateLimit.enabled";
    /// <summary>SysConfig 键:固定窗口时长(秒)。改值即时生效。</summary>
    public const string KEY_WINDOW = "sys.security.rateLimit.windowSeconds";
    /// <summary>SysConfig 键:用户端每窗口许可数。改值即时生效。</summary>
    public const string KEY_PERMIT = "sys.security.rateLimit.permitPerWindow";
    /// <summary>SysConfig 键:认证端点每窗口许可数(更严)。改值即时生效。</summary>
    public const string KEY_AUTH_PERMIT = "sys.security.rateLimit.authPermitPerWindow";

    /// <summary>部署期硬总开关;false 时无论 DB 如何都不限流。默认 true。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>固定窗口时长(秒),默认 60。运行时可由 <see cref="KEY_WINDOW"/> 覆盖。</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>用户端(按客户端 IP 分区)每窗口许可数,默认 300。运行时可由 <see cref="KEY_PERMIT"/> 覆盖。</summary>
    public int PermitPerWindow { get; set; } = 300;

    /// <summary>认证端点(登录/验证码等,更严)每窗口许可数,默认 20。运行时可由 <see cref="KEY_AUTH_PERMIT"/> 覆盖。</summary>
    public int AuthPermitPerWindow { get; set; } = 20;

    /// <summary>
    /// 机器端:带 API Key 头的请求按 key 计数,与用户端的 IP 桶分开;&lt;=0 不限。默认 600。
    /// 只在 Options 里配(设备端限额是部署期决定的,不走配置中心)。
    /// </summary>
    public int KeyPermitPerWindow { get; set; } = 600;
}

/// <summary>机器端 API Key 接入(对应 <c>SmartAdmin:Security:ApiKey</c>)。</summary>
public class AdminApiKeyOptions
{
    /// <summary>取 key 的请求头名。默认 <c>X-Api-Key</c>。</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>预共享密钥清单(默认 <c>IApiKeyValidator</c> 的数据源);要从数据库 / KMS 取就换实现,不用这里。</summary>
    public List<ApiKeyEntry> Keys { get; set; } = [];
}

/// <summary>一把 API Key。</summary>
public class ApiKeyEntry
{
    /// <summary>名字:进操作日志与限流分区,也是主体的 <c>unique_name</c>;不要用 key 本身当名字。</summary>
    public string Name { get; set; } = "";

    /// <summary>密钥明文(建议 32 字节以上随机串,经环境变量或密钥管理注入)。</summary>
    public string Key { get; set; } = "";

    /// <summary>绑定的用户 Id(可选):绑了,这把 key 调 <c>[RolePermission]</c> 端点时按该用户的角色与数据范围判。</summary>
    public long? UserId { get; set; }
}

/// <summary>短信验证码(对应 <c>SmartAdmin:Security:SmsOtp</c>)。与 TOTP 独立。</summary>
public class AdminSmsOtpOptions
{
    /// <summary>短信二次验证兜底开关(默认关)</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>短信免密登录兜底开关(默认关)</summary>
    public bool LoginEnabled { get; set; }

    /// <summary>验证码位数,默认 6。</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>验证码有效期(秒),默认 300。</summary>
    public int TtlSeconds { get; set; } = 300;

    /// <summary>两次发送的最小间隔(秒),默认 60。</summary>
    public int ResendSeconds { get; set; } = 60;

    /// <summary>验证码允许的错误尝试次数,默认 5;达到即作废该码。</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>单个手机号每日最大发送次数,默认 10。</summary>
    public int DailySendLimitPerPhone { get; set; } = 10;
}

/// <summary>
/// 口令哈希参数(对应 <c>SmartAdmin:Security:Password</c>)。
/// </summary>
public class AdminPasswordOptions
{
    /// <summary>PBKDF2 迭代次数下限:低于它按下限算,免得一次手滑把口令哈希削成明文级。</summary>
    public const int MIN_ITERATIONS = 100_000;

    /// <summary>
    /// PBKDF2-SHA256 迭代次数,默认 60 万(OWASP 当前建议值)。<b>低于 10 万按 10 万处理。</b>
    /// <para>单次计算约几十到一百毫秒,这是故意的开销:暴力破解者要按次付费。但它同样是<b>你自己</b>的
    /// CPU 开销——登录接口被刷时放大的是本机负载,弱硬件上可以下调,强硬件上可以上调。</para>
    /// <para>调整不影响存量:哈希串里记着自己那次用的迭代数,老口令照旧能验通过,并在下次登录时无感重算。</para>
    /// </summary>
    public int Pbkdf2Iterations { get; set; } = 600_000;
}

/// <summary>
/// 安全配置根节(对应 <c>SmartAdmin:Security</c>)。
/// 产品形状(ADR 0006):独立可选键,默认宽松;见仓库 <c>docs/agents/security-optional-config.md</c>。
/// </summary>
public class AdminSecurityOptions
{
    /// <summary>
    /// 认证请求未绑定数据范围时的回退策略。<b>默认 false = 空范围</b>(查不到任何受控行);
    /// <c>true</c> = 不受限,看全库。
    /// <para>正常路径上范围总会在授权阶段绑好,这个开关只影响异常路径的兜底方向:一个漏绑的端点
    /// 是"查不到数据"(能立刻发现)还是"看到全部机构的数据"(悄无声息)。匿名请求、后台任务、超管不受影响。</para>
    /// </summary>
    public bool DataScopeFailOpen { get; set; }

    /// <summary>TOTP 二因子(默认关)</summary>
    public AdminTotpOptions Totp { get; set; } = new();

    /// <summary>会话:Cookie 模式 / 并发 / 闲置 / 绝对寿命</summary>
    public AdminSessionOptions Session { get; set; } = new();

    /// <summary>登录失败锁定(见 <see cref="AdminLoginLockOptions"/>)。</summary>
    public AdminLoginLockOptions LoginLock { get; set; } = new();

    /// <summary>登录验证码(见 <see cref="AdminCaptchaOptions"/>)。</summary>
    public AdminCaptchaOptions Captcha { get; set; } = new();

    /// <summary>请求限流(见 <see cref="AdminRateLimitOptions"/>)。</summary>
    public AdminRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>机器端 API Key 接入(请求头名 + 预共享密钥清单)。</summary>
    public AdminApiKeyOptions ApiKey { get; set; } = new();

    /// <summary>短信验证码(见 <see cref="AdminSmsOtpOptions"/>)。</summary>
    public AdminSmsOtpOptions SmsOtp { get; set; } = new();

    /// <summary>数据保护主密钥(见 <see cref="AdminDataProtectionOptions"/>)。</summary>
    public AdminDataProtectionOptions DataProtection { get; set; } = new();

    /// <summary>口令哈希参数(见 <see cref="AdminPasswordOptions"/>)。口令<b>策略</b>(长度、字符类、有效期)在配置中心。</summary>
    public AdminPasswordOptions Password { get; set; } = new();

    /// <summary>
    /// 新建/重置未显式给密时的默认初始口令。null = 密码学随机强口令(推荐)。
    /// </summary>
    public string? DefaultInitialPassword { get; set; }

    // ── 有效能力判定 ──

    /// <summary>
    /// 部署配置下的 TOTP 能力(不含 SysConfig 运行时键)。
    /// 完整判定请用 <c>IMfaPolicyService.IsTotpFeatureEnabledAsync</c>(Options ∨ DB)。
    /// </summary>
    public bool IsTotpFeatureEnabled => Totp.Enabled;

    /// <summary>Cookie+CSRF 会话是否启用(<c>Session:CookieMode</c>)。</summary>
    public bool IsCookieSessionEnabled => Session.CookieMode;

    /// <summary>是否启用闲置会话过期(两档任一 &gt; 0)。</summary>
    public bool IsSessionIdleEnabled =>
        Session.IdleMinutesNormal > 0 || Session.IdleMinutesMfa > 0;

    /// <summary>是否启用绝对会话寿命(<c>AbsoluteHours</c> &gt; 0)。</summary>
    public bool IsSessionAbsoluteEnabled => Session.AbsoluteHours > 0;

    /// <summary>解析 Cookie Domain。空 = 当前 host。</summary>
    public string? ResolveCookieDomain() =>
        string.IsNullOrWhiteSpace(Session.CookieDomain) ? null : Session.CookieDomain.Trim();

    /// <summary>解析 TOTP issuer。</summary>
    public string ResolveTotpIssuer() =>
        string.IsNullOrWhiteSpace(Totp.Issuer) ? "SmartAdmin" : Totp.Issuer.Trim();

    /// <summary>解析 TOTP 挑战 TTL(秒)。</summary>
    public int ResolveTotpChallengeTtlSeconds() =>
        Totp.ChallengeTtlSeconds > 0 ? Totp.ChallengeTtlSeconds : 300;

    /// <summary>解析再认证窗口(分钟)。</summary>
    public int ResolveReauthWindowMinutes() =>
        Totp.ReauthWindowMinutes > 0 ? Totp.ReauthWindowMinutes : 5;

    /// <summary>解析闲置分钟数。<paramref name="mfaUser"/> 为 true 时优先 MFA 档;都没配则 0 = 不过期。</summary>
    public int ResolveIdleMinutes(bool mfaUser)
    {
        if (mfaUser && Session.IdleMinutesMfa > 0)
            return Session.IdleMinutesMfa;
        return Session.IdleMinutesNormal > 0 ? Session.IdleMinutesNormal : 0;
    }

    /// <summary>解析绝对会话寿命。未配置 = 无绝对上限。</summary>
    public TimeSpan? ResolveAbsoluteTimeSpan() =>
        Session.AbsoluteHours > 0 ? TimeSpan.FromHours(Session.AbsoluteHours) : null;
}
