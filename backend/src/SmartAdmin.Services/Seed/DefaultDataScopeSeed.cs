using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 默认角色数据范围种子。给 <see cref="DefaultRoleSeed"/> 播的五个示例角色(scope_all/org/org_children/self/custom)
/// 各配一条对应的数据范围,演示五种 <see cref="DataScopeType"/> 的效果。
/// </summary>
public class DefaultDataScopeSeed : ISeedData<SysRoleDataScope>
{
    /// <summary>连接表:唯一索引在 RoleId,代理主键会漂,按 RoleId 判存(见 <see cref="ISeedData{T}.DedupColumns"/>)。</summary>
    public string[] DedupColumns => [nameof(SysRoleDataScope.RoleId)];

    /// <inheritdoc />
    public virtual IEnumerable<SysRoleDataScope> HasData() =>
    [
        new SysRoleDataScope { Id = 1, RoleId = 2, ScopeType = DataScopeType.All,            CustomOrgIds = "" },
        new SysRoleDataScope { Id = 2, RoleId = 3, ScopeType = DataScopeType.Org,            CustomOrgIds = "" },
        new SysRoleDataScope { Id = 3, RoleId = 4, ScopeType = DataScopeType.OrgAndChildren, CustomOrgIds = "" },
        new SysRoleDataScope { Id = 4, RoleId = 5, ScopeType = DataScopeType.Self,           CustomOrgIds = "" },
        new SysRoleDataScope { Id = 5, RoleId = 6, ScopeType = DataScopeType.Custom,         CustomOrgIds = "2,6" },
    ];
}
