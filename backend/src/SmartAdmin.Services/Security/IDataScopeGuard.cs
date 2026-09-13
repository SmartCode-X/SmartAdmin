using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 数据范围在<b>系统表</b>上的收口点。
/// <para>为什么需要它:全局查询过滤器只认 <c>IOrgScoped</c>(业务表继承 <c>DataEntity</c> 即自动受控),
/// 而内核自己的 <c>Sys*</c> 表一律继承 <c>BaseEntity</c>——过滤器对它们一行都不生效。于是"谁能看见哪些用户/
/// 哪些日志/哪些会话"只能各服务手写,写漏一个就是一处越权面——操作日志、在线会话、通知列表都经它收口。</para>
/// <para>超管与不受限范围一律放行;<c>null</c> 机构视为在范围内(根级/未分配,沿用既有语义)。</para>
/// </summary>
public interface IDataScopeGuard
{
    /// <summary>当前调用者是否不受范围约束(超管,或范围为"全部")。</summary>
    bool IsUnrestricted { get; }

    /// <summary>机构是否在调用者范围内。</summary>
    bool IsOrgInScope(long? orgId);

    /// <summary>机构不在范围内即抛 <see cref="ErrorCode.OrgOutOfScope"/>。</summary>
    void EnsureOrgInScope(long? orgId);

    /// <summary>给用户查询叠上范围条件:机构在范围内,或(范围含"仅本人"时)就是调用者自己。</summary>
    ISugarQueryable<SysUser> ScopeUsers(ISugarQueryable<SysUser> users);

    /// <summary>
    /// 范围内的用户 Id 集合;不受限时返回 <c>null</c>(调用方据此跳过条件)。
    /// <para><b>含软删用户</b>:日志与会话是历史事实,离职的人做过什么不该随账号软删而消失。</para>
    /// </summary>
    Task<List<long>?> ResolveScopedUserIdsAsync();

    /// <summary>目标用户是否在调用者范围内(不受限恒真;用户不存在返回 false)。</summary>
    Task<bool> IsUserInScopeAsync(long userId);
}

/// <inheritdoc cref="IDataScopeGuard"/>
public class DataScopeGuard(
    IRepository<SysUser> users,
    ICurrentUser? currentUser = null,
    IDataScopeContext? dataScope = null) : IDataScopeGuard
{
    /// <summary>当前生效范围;无上下文时按不受限处理(后台任务、启动期)。</summary>
    protected DataScopeResult Scope => dataScope?.Current ?? DataScopeResult.Unrestricted;

    /// <inheritdoc />
    public virtual bool IsUnrestricted => currentUser?.IsSuperAdmin == true || Scope.IsUnrestricted;

    /// <inheritdoc />
    public virtual bool IsOrgInScope(long? orgId) =>
        IsUnrestricted || orgId is null || Scope.OrgIds.Contains(orgId.Value);

    /// <inheritdoc />
    public virtual void EnsureOrgInScope(long? orgId) =>
        AdminException.ThrowIf(!IsOrgInScope(orgId), ErrorCode.OrgOutOfScope);

    /// <inheritdoc />
    public virtual ISugarQueryable<SysUser> ScopeUsers(ISugarQueryable<SysUser> query)
    {
        if (IsUnrestricted) return query;

        var orgIds = Scope.OrgIds.ToList();
        var includeSelf = Scope.IncludeSelf;
        var selfId = currentUser?.UserId ?? 0;

        // 布尔标记写成 `== true` 而非裸布尔:SqlServer 的谓词上下文不接受裸标量,必须渲染成比较式。
        // 同 SqlSugarSetup 的全局数据范围过滤器。
        return query.Where(u =>
            (u.OrgId != null && orgIds.Contains(u.OrgId.Value))
            || (includeSelf == true && u.Id == selfId));
    }

    /// <inheritdoc />
    public virtual async Task<List<long>?> ResolveScopedUserIdsAsync()
    {
        if (IsUnrestricted) return null;
        // 清软删过滤器:日志/会话要能显示已离职用户产生的历史行
        return await ScopeUsers(users.AsQueryable().ClearFilter<ISoftDelete>()).Select(u => u.Id).ToListAsync();
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsUserInScopeAsync(long userId)
    {
        if (IsUnrestricted) return true;
        return await ScopeUsers(users.AsQueryable().ClearFilter<ISoftDelete>()).AnyAsync(u => u.Id == userId);
    }
}
