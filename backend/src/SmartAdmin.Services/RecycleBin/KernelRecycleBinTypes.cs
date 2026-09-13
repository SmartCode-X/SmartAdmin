using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 已删用户:列表按数据范围收敛(回收站里躺着的仍是用户行,与用户管理页同一条可见性规则,
/// 不该因为"删掉了"就对所有管理员敞开);恢复后重建门户代际;彻底删除前清角色关联与外部身份绑定。
/// </summary>
public class UserRecycleBinType() : RecycleBinType<SysUser>("user", e => e.Account + " / " + e.Name)
{
    /// <inheritdoc />
    protected override ISugarQueryable<SysUser> Query(IServiceProvider sp)
    {
        var scopedIds = sp.GetRequiredService<IDataScopeGuard>().ResolveScopedUserIdsAsync().GetAwaiter().GetResult();
        return base.Query(sp).WhereIF(scopedIds != null, e => scopedIds!.Contains(e.Id));
    }

    /// <inheritdoc />
    protected override Task AfterRestoreAsync(IServiceProvider sp, long id) =>
        sp.GetRequiredService<ICacheProvider>().IncrementAsync(CacheKeys.PortalGeneration);

    /// <inheritdoc />
    protected override async Task BeforePurgeAsync(IServiceProvider sp, long id)
    {
        await sp.GetRequiredService<ISqlSugarClient>().Deleteable<SysUserRole>()
            .Where(ur => ur.UserId == id).ExecuteCommandAsync();
        if (sp.GetService<ISysUserExternalService>() is { } bindings) await bindings.UnbindAllAsync(id);
    }
}

/// <summary>已删角色:恢复后失效持有该角色用户的权限缓存并重建门户代际;彻底删除前清授权关联。</summary>
public class RoleRecycleBinType() : RecycleBinType<SysRole>("role", e => e.Name, e => e.Code)
{
    /// <inheritdoc />
    protected override async Task AfterRestoreAsync(IServiceProvider sp, long id)
    {
        await sp.GetRequiredService<IRbacService>().InvalidateByRoleAsync(id);
        await sp.GetRequiredService<ICacheProvider>().IncrementAsync(CacheKeys.PortalGeneration);
    }

    /// <inheritdoc />
    protected override Task BeforePurgeAsync(IServiceProvider sp, long id) =>
        sp.GetRequiredService<IRbacService>().OnRoleDeletedAsync(id);
}

/// <summary>
/// 已删任务:恢复时<b>强制置 Paused</b>。恢复出来的行 NextRunTime 是删除时的过去时刻,
/// 直接放回 Ready 会被当成错过而立刻补跑/推进——人工在任务页 enable 才重算复跑,是恢复动作该有的语义。
/// </summary>
public class JobRecycleBinType() : RecycleBinType<SysJob>("job", e => e.Name, e => e.Code)
{
    /// <inheritdoc />
    protected override Task AfterRestoreAsync(IServiceProvider sp, long id) =>
        sp.GetRequiredService<ISqlSugarClient>().Updateable<SysJob>()
            .SetColumns(j => new SysJob { Status = JobStatus.Paused, NextRunTime = null })
            .Where(j => j.Id == id)
            .ExecuteCommandAsync();
}
