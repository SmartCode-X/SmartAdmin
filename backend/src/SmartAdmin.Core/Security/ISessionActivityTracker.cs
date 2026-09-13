namespace SmartAdmin.Core;

/// <summary>
/// 会话活动追踪(闲置判定的热路径)。
/// 每请求更新最近活动时间,经缓存节流后回写 DB——禁止每请求落库。
/// <para>启用闲置超时时缓存写失败应失败关闭(调用方视 <c>false</c> 为会话失活),不得静默跳过闲置判定。</para>
/// </summary>
public interface ISessionActivityTracker
{
    /// <summary>
    /// 记录一次活动。成功返回 true;启用闲置超时时缓存/回写关键路径失败返回 false(fail-closed)。
    /// 未启用闲置超时时失败仍返回 true(尽力而为,不阻断既有会话)。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="userId">用户 Id(回写会话行用)</param>
    /// <param name="sessionExpiresAt">会话过期时刻(刷新缓存 TTL)</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> TouchAsync(string sessionId, long userId, DateTime sessionExpiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 热路径重载:调用方刚把会话缓存读出来了,直接把那一份传进来,别在这里再读一遍。
    /// <para>默认实现回落到上面的重载(第三方实现不必改,只是省不掉那次读)。</para>
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cached">调用方已读出的会话缓存</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> TouchAsync(string sessionId, SessionCacheInfo cached, CancellationToken cancellationToken = default)
        => TouchAsync(sessionId, cached.UserId, cached.ExpiresAt, cancellationToken);
}
