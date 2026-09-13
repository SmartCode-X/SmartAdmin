using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="ISessionActivityTracker"/> 默认实现:更新会话缓存中的 LastActivityAt,
/// 并按 <see cref="AdminSessionOptions.ActivityThrottleSeconds"/> 节流回写 DB。
/// 启用闲置/Cookie 会话时缓存异常 → 返回 false(活动不可信);否则吞掉并返回 true。
/// <para><b>热路径上尽量不写</b>:节流时刻记在会话缓存自身的 <see cref="SessionCacheInfo.LastPersistedAt"/> 里,
/// 不落单独的键,省掉每请求多一次缓存读取;没开闲置超时时连缓存回写都省掉——那种配置下
/// <c>LastActivityAt</c> 只用于在线会话列表的展示,由节流窗口的那次 DB 回写供给即可。</para>
/// </summary>
public class SessionActivityTracker(
    ICacheProvider cache,
    IRepository<SysSession> sessions,
    AdminSecurityOptions security,
    TimeProvider time) : ISessionActivityTracker
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public virtual async Task<bool> TouchAsync(
        string sessionId, long userId, DateTime sessionExpiresAt, CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync<SessionCacheInfo>(CacheKeys.Session(sessionId), cancellationToken);
        // 缓存里没有(刚被驱逐)时只回写 DB:没有可更新的缓存条目,凭空造一个反而会把过期时刻编错
        return cached is null
            ? await PersistOnlyAsync(sessionId, cancellationToken)
            : await TouchAsync(sessionId, cached, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> TouchAsync(
        string sessionId, SessionCacheInfo cached, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = Now;
            var throttleSec = security.Session.ActivityThrottleSeconds > 0
                ? security.Session.ActivityThrottleSeconds
                : 60;
            var persist = cached.LastPersistedAt is not { } last
                          || (now - last).TotalSeconds >= throttleSec;

            if (persist)
            {
                await sessions.Db.Updateable<SysSession>()
                    .SetColumns(s => s.LastActivityAt == now)
                    .Where(s => s.SessionId == sessionId)
                    .ExecuteCommandAsync();
            }

            // 只在有人会读它的时候才回写缓存:开了闲置超时(每请求都要比对 LastActivityAt),
            // 或本次刚落库(要记住 LastPersistedAt,否则下一个请求又会落一次)。
            if (persist || cached.IdleMinutes > 0)
            {
                var ttl = cached.ExpiresAt - now;
                if (ttl > TimeSpan.Zero)
                {
                    var updated = cached with
                    {
                        LastActivityAt = now,
                        LastPersistedAt = persist ? now : cached.LastPersistedAt,
                    };
                    await cache.SetAsync(CacheKeys.Session(sessionId), updated, ttl, cancellationToken);
                }
            }

            return true;
        }
        catch
        {
            return !Strict;
        }
    }

    /// <summary>缓存缺失时的退化路径:只把活动时间落库,不重建缓存条目。</summary>
    protected virtual async Task<bool> PersistOnlyAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var now = Now;
            await sessions.Db.Updateable<SysSession>()
                .SetColumns(s => s.LastActivityAt == now)
                .Where(s => s.SessionId == sessionId)
                .ExecuteCommandAsync();
            return true;
        }
        catch
        {
            return !Strict;
        }
    }

    /// <summary>闲置或 Cookie 会话依赖活动跟踪:失败则 fail-closed;否则不阻断既有会话。</summary>
    private bool Strict => security.IsSessionIdleEnabled || security.IsCookieSessionEnabled;
}
