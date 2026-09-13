using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>示例机构树种子:一家公司 + 总经办/技术部(下设前端组、后端组)/产品部/人事部,供开箱即用演示机构与数据范围。</summary>
public class DefaultOrgSeed : ISeedData<SysOrg>
{
    /// <summary>根机构固定主键。</summary>
    internal const long ROOT_ORG_ID = 1;

    /// <inheritdoc />
    public virtual IEnumerable<SysOrg> HasData() =>
    [
        new SysOrg { Id = 1, ParentId = 0, Name = "SmartAdmin 科技", Code = "smart",       Category = "1", Sort = 1, Enabled = true },
        new SysOrg { Id = 2, ParentId = 1, Name = "总经办",   Code = "smart_gm",    Category = "2", Sort = 1, Enabled = true },
        new SysOrg { Id = 3, ParentId = 1, Name = "技术部",   Code = "smart_tech",  Category = "2", Sort = 2, Enabled = true },
        new SysOrg { Id = 4, ParentId = 3, Name = "前端组",   Code = "smart_fe",    Category = "3", Sort = 1, Enabled = true },
        new SysOrg { Id = 5, ParentId = 3, Name = "后端组",   Code = "smart_be",    Category = "3", Sort = 2, Enabled = true },
        new SysOrg { Id = 6, ParentId = 1, Name = "产品部",   Code = "smart_pm",    Category = "2", Sort = 3, Enabled = true },
        new SysOrg { Id = 7, ParentId = 1, Name = "人事部",   Code = "smart_hr",    Category = "2", Sort = 4, Enabled = true },
    ];
}
