using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 示例用户-角色关联种子:把 <see cref="DefaultUserSeed"/> 播的五个数据范围演示账号
/// (全部数据/本机构数据/本机构及以下/仅本人数据/自定义范围)各绑定 <see cref="DefaultRoleSeed"/> 播的同名角色,
/// 开箱即可登录对比不同数据范围的效果。
/// </summary>
public class DefaultUserRoleSeed : ISeedData<SysUserRole>
{
    /// <summary>连接表:代理主键是运行时雪花号、会漂,按 (UserId, RoleId) 判存(见 <see cref="ISeedData{T}.DedupColumns"/>)。</summary>
    public string[] DedupColumns => [nameof(SysUserRole.UserId), nameof(SysUserRole.RoleId)];

    /// <inheritdoc />
    public virtual IEnumerable<SysUserRole> HasData() =>
    [
        new SysUserRole { Id = 1, UserId = 2, RoleId = 2 },
        new SysUserRole { Id = 2, UserId = 3, RoleId = 3 },
        new SysUserRole { Id = 3, UserId = 4, RoleId = 4 },
        new SysUserRole { Id = 4, UserId = 5, RoleId = 5 },
        new SysUserRole { Id = 5, UserId = 6, RoleId = 6 },
    ];
}
