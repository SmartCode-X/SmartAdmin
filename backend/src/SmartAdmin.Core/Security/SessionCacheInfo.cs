namespace SmartAdmin.Core;

/// <summary>
/// 会话缓存值(热路径:每请求校验会话是否活跃,免每次查库)。
/// <para>在 Core 而不在 Services:<see cref="ISessionActivityTracker"/> 的热路径重载要按它传值,
/// 才能让"已经读出来的那一份"直接往下传,而不是每个环节各读一遍。</para>
/// </summary>
public record SessionCacheInfo
{
    /// <summary>用户 Id</summary>
    public required long UserId { get; init; }

    /// <summary>会话过期时刻(UTC);到点即判定不活跃</summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>绝对过期;未启用绝对寿命时与 ExpiresAt 对齐</summary>
    public DateTime AbsoluteExpiresAt { get; init; }

    /// <summary>最近活动(UTC);闲置判定用</summary>
    public DateTime? LastActivityAt { get; init; }

    /// <summary>
    /// 最近一次把活动时间回写进 DB 的时刻(UTC)。节流窗口就靠它算,
    /// 与会话本身共用同一份缓存读写,不必为节流单开一个缓存键。
    /// </summary>
    public DateTime? LastPersistedAt { get; init; }

    /// <summary>闲置超时分钟;0 = 不检闲置</summary>
    public int IdleMinutes { get; init; }

    /// <summary>是否按 MFA 会话策略(并发/闲置)</summary>
    public bool IsMfa { get; init; }
}
