using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// 会话与刷新令牌服务——登录建会话、每请求校验活跃、刷新轮换 + 复用检测、登出/强退、在线列表。
/// <para>类 public、方法 virtual,便于消费者继承覆写单个步骤而不必整体重写。</para>
/// </summary>
public interface ISessionService
{
    /// <summary>登录成功后开会话:落库 + 落缓存 + 存刷新令牌哈希;按单端/限并发策略吊销旧会话。</summary>
    Task OpenAsync(SysUser user, string sessionId, TokenPair pair);

    /// <summary>会话是否活跃(热路径:先读缓存,未命中查库回填)。强退/登出/过期后即为 false。</summary>
    Task<bool> IsActiveAsync(string sessionId);

    /// <summary>用刷新令牌换发新令牌对:校验 + 轮换(旧置 Used)+ 复用检测(重放整会话吊销)。失败抛 40007。</summary>
    Task<RefreshedSession> RefreshAsync(string refreshToken);

    /// <summary>吊销会话(登出 / 强退):标记会话与其刷新令牌失效 + 清缓存。原访问令牌下次请求即 401。</summary>
    Task RevokeAsync(string sessionId);

    /// <summary>
    /// 管理员强退某个会话,<b>带越权守卫</b>:目标用户须在调用者数据范围内,且非超管不得踢超管。
    /// <para>与 <see cref="RevokeAsync"/> 的区别只在守卫——后者是内部调用(自助登出、改密踢线),不该被这层挡住。
    /// 默认实现直接转发,使既有第三方实现无需改动即可编译;内置实现覆写为带守卫。</para>
    /// </summary>
    Task ForceLogoutAsync(string sessionId) => RevokeAsync(sessionId);

    /// <summary>
    /// 批量吊销一组会话。两条 UPDATE 覆盖全体、缓存一次批量删,而不是每个会话各来一遍。
    /// <para><b>覆写了 <see cref="RevokeAsync"/> 加副作用的消费者要一并覆写本方法</b>——
    /// 停用用户、改密踢线这些路径走的是它。默认接口实现仍逐个调 <see cref="RevokeAsync"/>,
    /// 所以只实现了接口而没继承内核实现的消费者行为不变。</para>
    /// </summary>
    Task RevokeManyAsync(IReadOnlyCollection<string> sessionIds) => RevokeEachAsync(sessionIds);

    /// <summary>默认逐个吊销兜底(供未覆写的实现复用)。</summary>
    protected async Task RevokeEachAsync(IReadOnlyCollection<string> sessionIds)
    {
        foreach (var id in sessionIds) await RevokeAsync(id);
    }

    /// <summary>吊销某用户的<b>全部</b>活跃会话(停用/删除用户时调用),使其持有的所有访问令牌下次请求即 401。</summary>
    Task RevokeAllForUserAsync(long userId);

    /// <summary>
    /// 吊销某用户的活跃会话,但保留 <paramref name="exceptSessionId"/>(若非空)——自助改密等场景下,
    /// 当前会话应保留以便本次 HTTP 请求正常返回,由前端据此自愿登出;其余会话立即失效。
    /// <para>默认实现直接吊销全部(不排除任何会话),使既有第三方 <see cref="ISessionService"/> 实现无需改动即可编译;
    /// 内置 <see cref="SessionService"/> 覆写为精确排除。</para>
    /// </summary>
    Task RevokeAllForUserExceptAsync(long userId, string? exceptSessionId) => RevokeAllForUserAsync(userId);

    /// <summary>在线会话分页(活跃且未过期)。</summary>
    Task<PagedList<OnlineSessionItem>> ListOnlineAsync(SessionPageInput input);
}
