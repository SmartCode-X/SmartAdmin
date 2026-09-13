using System.Security.Cryptography;
using System.Text;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="ISessionService"/> 默认实现。会话落库(源) + 落缓存(热路径),
/// 刷新令牌只存哈希;轮换用条件更新(仅当仍 Active 才置 Used)兼作并发保护,复用即整会话吊销。
/// 时间统一走 UTC(<see cref="TimeProvider"/>),避免本地/UTC 混用导致过期判断错乱。
/// <para><b>不需要进程内锁</b>:单端/限并发的名额收敛采「先插入、再收敛」(见 <see cref="EnforceConcurrencyAsync"/>)——
/// 并发登录都落库后各自重读、各自算出同一个"保留最新 N"的答案,天然收敛,不靠锁串行化;
/// 进程内锁只能护住本进程看到的状态,跨不了多副本部署,单端/并发上限的判断因此必须建立在
/// 落库后的重读之上,而不是内存里的一把锁。</para>
/// <para>会话绝对/闲置/并发由 <see cref="AdminSecurityOptions"/> 的独立键控制。</para>
/// </summary>
public class SessionService(
    IRepository<SysSession> sessions,
    IRepository<SysRefreshToken> refreshTokens,
    IRepository<SysUser> users,
    ITokenProvider tokens,
    ICacheProvider cache,
    AdminSecurityOptions security,
    ISecurityPolicyProvider policy,
    ICurrentUser currentUser,
    TimeProvider time,
    IRealtimePublisher? realtime = null,
    ISessionActivityTracker? activity = null,
    IReauthService? reauth = null,
    // 数据范围守卫;可选尾参,未注入时不收敛(纯 Services 宿主 / 未传该参的子类)
    IDataScopeGuard? scopeGuard = null) : ISessionService
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>高熵随机串的哈希:SHA-256 十六进制(不是密码,无需 PBKDF2)。</summary>
    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <inheritdoc />
    public virtual async Task OpenAsync(SysUser user, string sessionId, TokenPair pair)
    {
        var expiresAt = pair.RefreshExpiresAt.UtcDateTime;
        var now = Now;
        // 绝对窗:Session:AbsoluteHours;未启用则与 refresh 对齐
        var absSpan = security.ResolveAbsoluteTimeSpan();
        var absolute = absSpan is { } ts
            ? Min(expiresAt, now.Add(ts))
            : expiresAt;
        if (absolute > expiresAt) absolute = expiresAt;

        var isMfa = IsMfaUser(user);
        var idleMinutes = ResolveIdleMinutes(isMfa);

        // 会话行 + 刷新令牌成对写入包事务:半写不留"在线却不可刷新"的僵尸会话
        await sessions.Db.RunInTransactionAsync(async () =>
        {
            // IP/UA 取自当前登录请求(与 LogService 同款,登录前即可读到);会话行是登录时快照,刷新不重写
            await sessions.InsertAsync(new SysSession
            {
                SessionId = sessionId,
                UserId = user.Id,
                Account = user.Account,
                Ip = currentUser.IpAddress,
                UserAgent = currentUser.UserAgent,
                ExpiresAt = expiresAt,
                AbsoluteExpiresAt = absolute,
                LastActivityAt = now,
            });
            await refreshTokens.InsertAsync(new SysRefreshToken
            {
                SessionId = sessionId,
                UserId = user.Id,
                TokenHash = Sha256Hex(pair.RefreshToken),
                ExpiresAt = expiresAt,
                Status = RefreshTokenStatus.Active,
            });
        });
        await CacheActiveAsync(sessionId, user.Id, expiresAt, absolute, now, idleMinutes, isMfa);

        // 先插入、再收敛(见类注释):插完才腾位,并发的两个登录都能看见对方的新行,于是都算出同一个
        // "只保留最新 N 个"的答案 → 收敛到上限。不需要锁,因而也跨得了进程。
        await EnforceConcurrencyAsync(user.Id, user);
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsActiveAsync(string sessionId)
    {
        var key = CacheKeys.Session(sessionId);
        var cached = await cache.GetAsync<SessionCacheInfo>(key);
        if (cached is not null)
        {
            if (!IsSessionStillValid(cached.ExpiresAt, cached.AbsoluteExpiresAt, cached.LastActivityAt, cached.IdleMinutes))
                return false;
            // 活动回写:传已读出的这一份,tracker 不必再读一遍(热路径每请求都走这里)
            if (activity is not null && !await activity.TouchAsync(sessionId, cached))
                return false;
            return true;
        }

        // 未命中:查库判定(可能是被驱逐、或本进程没缓存过),活跃则回填
        var session = await sessions.GetFirstAsync(s => s.SessionId == sessionId);
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= Now) return false;

        var absolute = session.AbsoluteExpiresAt == default ? session.ExpiresAt : session.AbsoluteExpiresAt;
        var user = await users.GetByIdAsync(session.UserId);
        var isMfa = user is not null && IsMfaUser(user);
        var idleMinutes = ResolveIdleMinutes(isMfa);

        if (!IsSessionStillValid(session.ExpiresAt, absolute, session.LastActivityAt, idleMinutes))
            return false;

        var filled = await CacheActiveAsync(
            sessionId, session.UserId, session.ExpiresAt, absolute, session.LastActivityAt ?? Now, idleMinutes, isMfa);

        if (activity is not null && !await activity.TouchAsync(sessionId, filled))
            return false;
        return true;
    }

    /// <inheritdoc />
    public virtual async Task<RefreshedSession> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        var rt = await refreshTokens.GetFirstAsync(t => t.TokenHash == Sha256Hex(refreshToken));
        if (rt is null) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 复用检测:已轮换令牌再现 = 重放,吊销整会话(攻击者与真用户一起下线,安全优先)
        if (rt.Status == RefreshTokenStatus.Used)
        {
            await RevokeAsync(rt.SessionId);
            throw new AdminException(ErrorCode.RefreshTokenInvalid);
        }
        if (rt.Status != RefreshTokenStatus.Active || rt.ExpiresAt <= Now)
            throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 会话须仍活跃(未强退/未过期/未绝对窗/未闲置)
        if (!await IsActiveAsync(rt.SessionId)) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        var user = await users.GetByIdAsync(rt.UserId);
        if (user is null) throw new AdminException(ErrorCode.RefreshTokenInvalid);
        AdminException.ThrowIf(!user.Enabled, ErrorCode.AccountDisabled);

        var session = await sessions.GetFirstAsync(s => s.SessionId == rt.SessionId);
        if (session is null) throw new AdminException(ErrorCode.RefreshTokenInvalid);
        var absolute = session.AbsoluteExpiresAt == default ? session.ExpiresAt : session.AbsoluteExpiresAt;
        if (absolute <= Now) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 原子轮换:仅当仍 Active 才置 Used;rowsAffected==0 说明已被并发轮换,按无效处理
        var rotated = await refreshTokens.Db.Updateable<SysRefreshToken>()
            .SetColumns(t => t.Status == RefreshTokenStatus.Used)
            .Where(t => t.Id == rt.Id && t.Status == RefreshTokenStatus.Active)
            .ExecuteCommandAsync();
        if (rotated == 0) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        // 用同一 SessionId 签发新令牌对(会话延续,不新建);令牌时长运行时可配,且不得突破绝对窗
        var (accessMin, refreshMin) = await policy.GetSessionTtlAsync();
        var maxRemain = absolute - Now;
        if (maxRemain <= TimeSpan.Zero) throw new AdminException(ErrorCode.RefreshTokenInvalid);

        var accessTtl = TimeSpan.FromMinutes(accessMin);
        if (accessTtl > maxRemain) accessTtl = maxRemain;
        var refreshTtl = TimeSpan.FromMinutes(refreshMin);
        if (refreshTtl > maxRemain) refreshTtl = maxRemain;

        var pair = tokens.Create(new TokenSubject(user.Id, user.Account, rt.SessionId, user.IsSuperAdmin, user.OrgId),
            accessTtl, refreshTtl);
        var expiresAt = Min(pair.RefreshExpiresAt.UtcDateTime, absolute);

        await refreshTokens.InsertAsync(new SysRefreshToken
        {
            SessionId = rt.SessionId,
            UserId = user.Id,
            TokenHash = Sha256Hex(pair.RefreshToken),
            ExpiresAt = expiresAt,
            Status = RefreshTokenStatus.Active,
        });

        // 滑动续期:会话过期跟到新刷新令牌过期(仍不超过绝对窗),缓存同步刷新
        var isMfa = IsMfaUser(user);
        var idleMinutes = ResolveIdleMinutes(isMfa);
        var lastAct = Now;
        await sessions.Db.Updateable<SysSession>()
            .SetColumns(s => s.ExpiresAt == expiresAt)
            .SetColumns(s => s.LastActivityAt == lastAct)
            .Where(s => s.SessionId == rt.SessionId)
            .ExecuteCommandAsync();
        await CacheActiveAsync(rt.SessionId, user.Id, expiresAt, absolute, lastAct, idleMinutes, isMfa);

        // 若签发结果的 RefreshExpiresAt 晚于绝对窗,收紧出参(前端/Cookie 以服务端为准)
        if (pair.RefreshExpiresAt.UtcDateTime > absolute)
            pair = pair with { RefreshExpiresAt = new DateTimeOffset(absolute, TimeSpan.Zero) };
        if (pair.ExpiresAt.UtcDateTime > absolute)
            pair = pair with { ExpiresAt = new DateTimeOffset(Min(pair.ExpiresAt.UtcDateTime, absolute), TimeSpan.Zero) };

        return new RefreshedSession(pair, user);
    }

    /// <inheritdoc />
    public virtual async Task RevokeAsync(string sessionId)
    {
        await MarkRevokedAsync([sessionId]);
        await cache.RemoveAsync(CacheKeys.Session(sessionId));   // 缓存移除 → 下次校验查库得吊销 → 401
        // 吊销该会话上的 reauth 窗口(避免跨会话复用后 sid 已死仍残留——按 sid 清)
        long? userId = null;
        if (reauth is not null)
            userId = (await sessions.GetFirstAsync(s => s.SessionId == sessionId))?.UserId;
        await AfterRevokedAsync(sessionId, userId);
    }

    /// <inheritdoc />
    public virtual async Task RevokeManyAsync(IReadOnlyCollection<string> sessionIds)
    {
        if (sessionIds.Count == 0) return;
        await RevokeManyAsync(sessionIds.Select(id => (id, (long?)null)).ToList());
    }

    /// <summary>
    /// 批量吊销,并带上各会话的 UserId(调用方多半刚把行查出来,别再为 reauth 逐个回查一次)。
    /// </summary>
    protected virtual async Task RevokeManyAsync(IReadOnlyCollection<(string SessionId, long? UserId)> targets)
    {
        if (targets.Count == 0) return;
        var ids = targets.Select(t => t.SessionId).ToList();

        await MarkRevokedAsync(ids);                                        // 两条 UPDATE 覆盖全体
        await cache.RemoveManyAsync(ids.Select(CacheKeys.Session));         // 缓存一次批量删

        // 剩下的收尾不碰 DB(reauth 键、实时推送),逐个跑
        foreach (var (sessionId, userId) in targets)
            await AfterRevokedAsync(sessionId, userId);
    }

    /// <summary>把会话行与其刷新令牌标记为已吊销。一组会话共两条 UPDATE。</summary>
    protected virtual async Task MarkRevokedAsync(IReadOnlyCollection<string> sessionIds)
    {
        // 拷到局部:SqlSugar 翻译不了对属性/字段的 Contains(必须是局部变量)
        var ids = sessionIds as List<string> ?? [.. sessionIds];
        var now = Now;
        await sessions.Db.Updateable<SysSession>()
            .SetColumns(s => s.RevokedAt == now)
            .Where(s => ids.Contains(s.SessionId) && s.RevokedAt == null)
            .ExecuteCommandAsync();
        await refreshTokens.Db.Updateable<SysRefreshToken>()
            .SetColumns(t => t.Status == RefreshTokenStatus.Revoked)
            .Where(t => ids.Contains(t.SessionId) && t.Status == RefreshTokenStatus.Active)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 单个会话下线后的非 DB 收尾:清 reauth 窗口 + 实时把在线连接踢掉,不必等它下次请求才吃 401。
    /// 所有下线路径(强退/超并发收敛/刷新复用/停用删号)都汇聚到此,一处接线全覆盖。
    /// </summary>
    protected virtual async Task AfterRevokedAsync(string sessionId, long? userId)
    {
        if (reauth is not null && userId is { } uid)
            await reauth.RevokeAsync(uid, sessionId);
        if (realtime is not null)
            await realtime.NotifySessionAsync(sessionId, "force-logout");
    }

    /// <inheritdoc />
    public virtual async Task RevokeAllForUserAsync(long userId)
    {
        // 与 EnforceConcurrencyAsync 同款"按 userId 取活跃会话再逐个吊销",少了单端/限并发的名额判断——
        // 停用/删除用户要下线其全部会话。逐个 RevokeAsync 复用其"标记两表 + 清会话缓存"逻辑。
        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .Select(s => s.SessionId)
            .ToListAsync();
        // 逐个 reauth 清理省掉:下面 RevokeAllForUserAsync 会把该用户的窗口整体清掉
        await RevokeManyAsync(active.Select(id => (id, (long?)null)).ToList());
        if (reauth is not null)
            await reauth.RevokeAllForUserAsync(userId);
    }

    /// <inheritdoc />
    public virtual async Task RevokeAllForUserExceptAsync(long userId, string? exceptSessionId)
    {
        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .Select(s => s.SessionId)
            .ToListAsync();
        var targets = active
            .Where(id => string.IsNullOrEmpty(exceptSessionId) || id != exceptSessionId)
            .Select(id => (id, (long?)userId))
            .ToList();
        await RevokeManyAsync(targets);
    }

    /// <inheritdoc />
    public virtual async Task<PagedList<OnlineSessionItem>> ListOnlineAsync(SessionPageInput input)
    {
        // 在线列表按数据范围收敛:非超管只看得见自己范围内用户的会话——否则一个子管理员既能看到
        // 全公司谁在线、从哪个 IP 登的,又能顺手强退他们。
        var scopedIds = scopeGuard is null ? null : await scopeGuard.ResolveScopedUserIdsAsync();
        return await sessions.AsQueryable()
            .Where(s => s.RevokedAt == null && s.ExpiresAt > Now)
            .WhereIF(scopedIds != null, s => scopedIds!.Contains(s.UserId))
            .WhereIF(input.UserId.HasValue, s => s.UserId == input.UserId)
            .OrderByDescending(s => s.CreateTime)
            .Select(s => new OnlineSessionItem
            {
                SessionId = s.SessionId,
                UserId = s.UserId,
                Account = s.Account,
                Ip = s.Ip,
                UserAgent = s.UserAgent,
                LoginTime = s.CreateTime,
                ExpiresAt = s.ExpiresAt,
            })
            .ToPagedListAsync(input.Current, input.Size);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 强退的越权守卫。会话<b>不存在</b>时幂等返回成功(管理员点的那一行可能刚过期,不该报错);
    /// 会话存在但不在操作者数据范围内,或非超管操作者去踢超管,则抛 <see cref="ErrorCode.SessionNotFound"/>——
    /// 越界时若默默返回成功,管理员会误以为已经踢掉,对方其实还在线。两种拒绝原因共用一个码、
    /// 响应不暴露具体是哪一种:会话 id 是高熵随机串,"不存在则成功 / 存在但越界则报错"这点差异
    /// 不构成可枚举的探测面。
    /// </remarks>
    public virtual async Task ForceLogoutAsync(string sessionId)
    {
        var target = await sessions.GetFirstAsync(x => x.SessionId == sessionId);
        if (target is null)
        {
            await RevokeAsync(sessionId);   // 会话已不存在:吊销 0 行受影响,按幂等处理返回成功
            return;
        }

        if (scopeGuard is not null && !scopeGuard.IsUnrestricted)
        {
            AdminException.ThrowIf(!await scopeGuard.IsUserInScopeAsync(target.UserId), ErrorCode.SessionNotFound);
            // 非超管不得踢超管:范围之外还有一层身份高度,越权面比机构维度更大
            var targetUser = await users.AsQueryable().ClearFilter<ISoftDelete>()
                .Where(u => u.Id == target.UserId).Select(u => new { u.IsSuperAdmin }).FirstAsync();
            AdminException.ThrowIf(targetUser?.IsSuperAdmin == true, ErrorCode.SessionNotFound);
        }

        await RevokeAsync(sessionId);
    }

    /// <summary>
    /// 按单端/限并发策略收敛活跃会话:<b>只保留最新的 N 个,其余一律吊销</b>。
    /// <para>在新会话<b>插入之后</b>调用(见 <see cref="OpenAsync"/>)。这是关键:并发的两个登录都已落库,
    /// 于是都读得到对方的行、都算出同一个"保留最新 N"的答案 → 收敛到上限。吊销幂等(只写未吊销的行),
    /// 重复吊销无害,因此<b>不需要任何锁</b>——进程内锁只能护住本进程看到的状态,跨不了多副本部署。</para>
    /// <para>排序以 <c>CreateTime</c> 为准,同毫秒用雪花 <c>Id</c> 决胜(单调递增),保证各副本算出的顺序一致。</para>
    /// </summary>
    protected virtual async Task EnforceConcurrencyAsync(long userId, SysUser? userHint = null)
    {
        var keep = ResolveKeepCount(userHint is not null ? IsMfaUser(userHint) : await IsMfaUserIdAsync(userId));
        if (keep <= 0) return;   // 多端不限

        var active = await sessions.AsQueryable()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > Now)
            .OrderByDescending(s => s.CreateTime)             // 最新在前
            .OrderByDescending(s => s.Id)                     // 同毫秒:雪花 Id 决胜(各副本口径一致)
            .ToListAsync();

        foreach (var s in active.Skip(keep))                  // 超出名额的(最旧的那些)一律吊销
            await RevokeAsync(s.SessionId);
    }

    /// <summary>解析应保留的并发名额;0 = 不限(Multi 且 MaxConcurrent≤0)。</summary>
    protected virtual int ResolveKeepCount(bool isMfa)
    {
        var mode = security.Session.Mode;
        var max = security.Session.MaxConcurrent;

        if (mode != SessionMode.Single && max <= 0) return 0;   // 多端不限
        return mode == SessionMode.Single ? 1 : max;
    }

    private async Task<bool> IsMfaUserIdAsync(long userId)
    {
        var user = await users.GetByIdAsync(userId);
        return user is not null && IsMfaUser(user);
    }

    /// <summary>是否按 MFA 会话策略(已绑 TOTP 或被强制 TOTP)。</summary>
    protected virtual bool IsMfaUser(SysUser user) => user.TotpEnabled || user.ForceTotp;

    /// <summary>闲置分钟:<c>Session:IdleMinutes*</c>,0 = 不启用闲置过期。</summary>
    protected virtual int ResolveIdleMinutes(bool isMfa) => security.ResolveIdleMinutes(isMfa);

    /// <summary>滑动过期 / 绝对窗 / 闲置三重判定。</summary>
    protected virtual bool IsSessionStillValid(
        DateTime expiresAt, DateTime absoluteExpiresAt, DateTime? lastActivityAt, int idleMinutes)
    {
        var now = Now;
        if (expiresAt <= now) return false;
        var absolute = absoluteExpiresAt == default ? expiresAt : absoluteExpiresAt;
        if (absolute <= now) return false;
        if (idleMinutes > 0)
        {
            var last = lastActivityAt ?? now; // 无活动记录时用 now 避免误杀(Open 已写 LastActivityAt)
            if (last.AddMinutes(idleMinutes) <= now) return false;
        }
        return true;
    }

    /// <summary>写入会话缓存并把写进去的那一份返回,调用方接着用,不必再读一次。</summary>
    private async Task<SessionCacheInfo> CacheActiveAsync(
        string sessionId, long userId, DateTime expiresAt, DateTime absoluteExpiresAt,
        DateTime lastActivityAt, int idleMinutes, bool isMfa)
    {
        var info = new SessionCacheInfo
        {
            UserId = userId,
            ExpiresAt = expiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
            LastActivityAt = lastActivityAt,
            IdleMinutes = idleMinutes,
            IsMfa = isMfa,
        };
        var ttl = expiresAt - Now;
        if (ttl > TimeSpan.Zero)
            await cache.SetAsync(CacheKeys.Session(sessionId), info, ttl);
        return info;
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;
}
