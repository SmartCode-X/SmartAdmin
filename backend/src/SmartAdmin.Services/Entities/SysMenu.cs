using SqlSugar;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 菜单/权限表——目录/页面/按钮三级树(<see cref="MenuType"/>)。
/// <para>关键字段 <see cref="Permission"/>:节点绑定的<b>规范化路由权限码</b>(如 <c>GET:/api/v1/ping</c>),
/// 与 <c>[RolePermission]</c> 授权管道算出的码一致——不手写 <c>"sys:user:add"</c> 之类魔法串。
/// 一个按钮可挂多条码(<c>;</c> 分隔),角色勾选该按钮即一并授出;用户权限码 = 所属角色菜单的 Permission 展开后的并集。</para>
/// <para>Path/Component/Icon/Visible 等前端展示字段只供动态路由与侧栏,后端授权不依赖它们。</para>
/// </summary>
[SugarTable("sys_menu", TableDescription = "菜单/权限")]
public class SysMenu : BaseEntity
{
    /// <summary>父节点 Id;0 表示根节点(顶级目录)</summary>
    [SugarColumn(ColumnDescription = "父菜单 Id(0=根)")]
    public long ParentId { get; set; }

    [SugarColumn(ColumnDescription = "节点类型(目录/页面/按钮)")]
    public MenuType Type { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "显示标题")]
    public string Title { get; set; } = "";

    /// <summary>
    /// 规范化路由权限码,形如 <c>GET:/api/v1/ping</c>(大写 Method + 冒号 + 小写路由模板);多条以 <c>;</c> 连接
    /// (拆合规则见 <c>PermissionCode.Split</c> / <c>Join</c>)。目录/页面节点为空;按钮节点带码。授权即比对请求路由是否在其中。
    /// </summary>
    [SugarColumn(Length = 512, ColumnDescription = "路由权限码(多条以 ; 分隔;目录为空)")]
    public string Permission { get; set; } = "";

    [SugarColumn(ColumnDescription = "排序(小在前)")]
    public int Sort { get; set; }

    [SugarColumn(ColumnDescription = "是否启用(停用后其权限码不再授出)")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 所属模块 Id(多应用门户)。<b>仅顶级目录(<c>ParentId==0</c>)设置</b>;子节点为 null,
    /// 归属由内存树上溯到根目录解析(见 <c>MenuService</c>)。为 null 的顶级目录 = 未分配到任何应用。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "所属模块 Id(仅顶级目录设置)")]
    public long? ModuleId { get; set; }

    // ── 前端展示字段(动态路由与侧栏用;后端授权不依赖这些)──────────────────────
    /// <summary>前端路由路径(页面节点用;目录/按钮可空)</summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "前端路由路径")]
    public string? Path { get; set; }

    /// <summary>前端页面 key(页面节点用):<c>views/</c> 之后去掉 <c>.vue</c> 的路径,如 <c>system/user/index</c></summary>
    [SugarColumn(Length = 256, IsNullable = true, ColumnDescription = "前端组件路径")]
    public string? Component { get; set; }

    /// <summary>菜单图标</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "图标")]
    public string? Icon { get; set; }

    /// <summary>是否在侧边栏可见(隐藏时路由仍注册,只是不显示在菜单)</summary>
    [SugarColumn(ColumnDescription = "是否在侧边栏可见")]
    public bool Visible { get; set; } = true;
}
