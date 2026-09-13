using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 基础菜单种子。只播<b>当前后端真实存在的受保护接口</b>对应的节点,不预埋尚不存在页面的菜单
/// (避免种子超前于实现);后续模块落地时同批增补自己的菜单节点。
/// </summary>
/// <remarks>
/// <para><b>权限按钮的粒度是能力,不是路由。</b>每个页面固定四颗:查询 / 新增 / 更新 / 删除,再加该页真实存在的
/// 独立操作(导入、导出、启停、重置密码、复制、执行一次……)。一颗按钮可挂多条路由码(以 <c>;</c> 连接,
/// 见 <see cref="PermissionCode.Separator"/>),角色勾上它即一并授出:
/// 「查询」覆盖列表与详情两个接口(外加该页表单要用的只读数据源,如任务处理器清单、菜单路由清单);
/// 「删除」含批量删除;「更新」含批量存值;「导入」把向导四步收成一颗;「上传」含分片三步。
/// 前端 <c>v-auth</c> 仍按单条路由码门控按钮,不受合并影响。</para>
/// <para>权限码必须与 <c>[RolePermission]</c> 授权管道算出的规范化路由一字不差
/// (大写 Method + 冒号 + 小写路由模板),否则授了也匹配不上;<c>PermissionCodeConsistencyTests</c> 双向锁死:
/// 种子里每条码都对应真实端点,每个受权端点都出现在某颗按钮里。</para>
/// <para>菜单树顶级节点:system 模块下工作台(根级页面)+ 组织管理 / 系统运维 / 任务调度 / 日志审计 / 文件管理五个目录;
/// 另有示例 business 模块下一条工作台(ModuleId 仅顶级节点设)。</para>
/// <para>层级约定:目录 → 页面 → 按钮。<b>按钮挂在它所属的页面下</b>(而非目录),这样菜单管理页里
/// 每个页面的权限按钮一目了然。<b>没有页面的接口</b>(只给移动端 / PDA / 第三方系统调的)把按钮直接挂在目录下,
/// 目录就是它们的权限组(如 301 探针挂在系统运维下):不生成路由、不进侧栏,角色授权界面把它们渲染成
/// 该目录组里「接口权限(无页面)」一行,照常勾选;只被授了这类权限的用户不会在门户看到该应用。</para>
/// <para><b>Id 编号</b>:百位是分区,1xx 工作台、2xx 组织管理、3xx 系统运维、4xx 任务调度、5xx 日志审计、6xx 文件管理,
/// 7xx–9xx 留给新目录。整百是目录本身,目录下无页面的接口按钮取 01–09(如 301 探针);页面取整十,一页独占一个十位段;
/// 根级页面(各应用的工作台)在 1xx 各占一个十位段。按钮的个位有固定语义:<b>1 查询、2 新增、3 更新、4 删除</b>,
/// 5–9 是本页特有操作;页面没有的标准按钮空着位,不往前挪。规则由 <c>MenuSeedIdLayoutTests</c> 锁定;
/// 撞号、越界由启动检查(<c>DatabaseInitializer</c>)与 <c>SeedIdRangeTests</c> 当场拒绝。</para>
/// </remarks>
public class DefaultMenuSeed : ISeedData<SysMenu>
{
    /// <summary>菜单树的<b>结构</b>是内核拥有的:内核升级时把已有节点的结构列刷回种子值(挪挂载点、改图标/路由/权限码
    /// 都靠这个到老库)。只刷 <see cref="SyncColumns"/> 里那几列,用户在菜单管理页改过的标题、排序、可见、启用照常留着。
    /// 见 <see cref="ISeedData{T}.SyncOnUpgrade"/>。</summary>
    public virtual bool SyncOnUpgrade => true;

    /// <summary>
    /// 升级时只刷这几列:挂载点、类型、权限码、路由、组件、图标、所属应用——它们决定"这个节点是什么、指向哪",
    /// 由内核定义。<c>Title</c> / <c>Sort</c> / <c>Visible</c> / <c>Enabled</c> 是用户在菜单管理页会改的,不在内。
    /// </summary>
    public virtual string[]? SyncColumns =>
    [
        nameof(SysMenu.ParentId), nameof(SysMenu.Type), nameof(SysMenu.Permission),
        nameof(SysMenu.Path), nameof(SysMenu.Component), nameof(SysMenu.Icon), nameof(SysMenu.ModuleId),
    ];

    /// <summary>多条路由码拼成一颗按钮的 Permission。</summary>
    private static string Codes(params string[] codes) => PermissionCode.Join(codes);

    /// <inheritdoc />
    public virtual IEnumerable<SysMenu> HasData() =>
    [
        // ═══ 1xx 工作台 ═════════════════════════════════════════════
        // 每个应用有自己的首页:工作台是一条普通菜单(根级 Menu 节点,故可挂 ModuleId),不是全局静态页。
        // Sort=0 → 排在所有目录前,也让它成为菜单树首个叶子(前端 homePath() 的兜底落点)。
        // 该页不打任何后端接口,故 Permission 为空。
        new SysMenu { Id = 100, ParentId = 0, Type = MenuType.Menu, Title = "工作台", Permission = "", Path = "/workbench", Component = "dashboard/workbench", Icon = "ph:squares-four-duotone", Sort = 0, Enabled = true, Visible = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        // 示例 business 模块(Id=2)的工作台:复用现成的 dashboard/biz.vue,Path 与 system 工作台错开。
        new SysMenu { Id = 110, ParentId = 0, Type = MenuType.Menu, Title = "工作台", Permission = "", Path = "/business/workbench", Component = "dashboard/biz", Icon = "ph:squares-four-duotone", Sort = 0, Enabled = true, Visible = true, ModuleId = DefaultModuleSeed.BUSINESS_MODULE_ID },

        // ═══ 2xx 组织管理 ═══════════════════════════════════════════
        new SysMenu { Id = 200, ParentId = 0, Type = MenuType.Catalog, Title = "组织管理", Permission = "", Icon = "ph:buildings-duotone", Sort = 1, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 机构管理页(OrgController 树 CRUD)。
        new SysMenu { Id = 210, ParentId = 200, Type = MenuType.Menu, Title = "机构管理", Permission = "", Path = "/system/org", Component = "system/org/index", Icon = "ph:tree-structure-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 211, ParentId = 210, Type = MenuType.Button, Title = "机构-查询", Permission = Codes("GET:/api/v1/sys/org/list", "GET:/api/v1/sys/org/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 212, ParentId = 210, Type = MenuType.Button, Title = "机构-新增", Permission = "POST:/api/v1/sys/org/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 213, ParentId = 210, Type = MenuType.Button, Title = "机构-更新", Permission = "PUT:/api/v1/sys/org/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 214, ParentId = 210, Type = MenuType.Button, Title = "机构-删除", Permission = "DELETE:/api/v1/sys/org/{id}", Sort = 4, Enabled = true },
        new SysMenu { Id = 215, ParentId = 210, Type = MenuType.Button, Title = "机构-复制", Permission = "POST:/api/v1/sys/org/{id}/copy", Sort = 5, Enabled = true },

        // 岗位管理页(PositionController 普通 CRUD)。
        new SysMenu { Id = 220, ParentId = 200, Type = MenuType.Menu, Title = "岗位管理", Permission = "", Path = "/system/position", Component = "system/position/index", Icon = "ph:identification-badge-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 221, ParentId = 220, Type = MenuType.Button, Title = "岗位-查询", Permission = Codes("GET:/api/v1/sys/position/page", "GET:/api/v1/sys/position/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 222, ParentId = 220, Type = MenuType.Button, Title = "岗位-新增", Permission = "POST:/api/v1/sys/position/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 223, ParentId = 220, Type = MenuType.Button, Title = "岗位-更新", Permission = "PUT:/api/v1/sys/position/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 224, ParentId = 220, Type = MenuType.Button, Title = "岗位-删除", Permission = "DELETE:/api/v1/sys/position/{id}", Sort = 4, Enabled = true },

        // 用户管理页(UserController)。导入模板下载走 [ActiveSession],无需权限节点。
        new SysMenu { Id = 230, ParentId = 200, Type = MenuType.Menu, Title = "用户管理", Permission = "", Path = "/system/user", Component = "system/user/index", Icon = "ph:users-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 231, ParentId = 230, Type = MenuType.Button, Title = "用户-查询", Permission = Codes("GET:/api/v1/sys/user/page", "GET:/api/v1/sys/user/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 232, ParentId = 230, Type = MenuType.Button, Title = "用户-新增", Permission = "POST:/api/v1/sys/user", Sort = 2, Enabled = true },
        new SysMenu { Id = 233, ParentId = 230, Type = MenuType.Button, Title = "用户-更新", Permission = "PUT:/api/v1/sys/user/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 234, ParentId = 230, Type = MenuType.Button, Title = "用户-删除", Permission = Codes("DELETE:/api/v1/sys/user/{id}", "POST:/api/v1/sys/user/batch-delete"), Sort = 4, Enabled = true },
        new SysMenu { Id = 235, ParentId = 230, Type = MenuType.Button, Title = "用户-重置密码", Permission = "PUT:/api/v1/sys/user/{id}/password", Sort = 5, Enabled = true },
        new SysMenu { Id = 236, ParentId = 230, Type = MenuType.Button, Title = "用户-启停", Permission = "PUT:/api/v1/sys/user/{id}/enabled", Sort = 6, Enabled = true },
        new SysMenu { Id = 237, ParentId = 230, Type = MenuType.Button, Title = "用户-导入", Permission = Codes("POST:/api/v1/sys/user/import/preview", "POST:/api/v1/sys/user/import/validate", "POST:/api/v1/sys/user/import/error-report", "POST:/api/v1/sys/user/import/commit"), Sort = 7, Enabled = true },
        new SysMenu { Id = 238, ParentId = 230, Type = MenuType.Button, Title = "用户-导出", Permission = "GET:/api/v1/sys/user/export", Sort = 8, Enabled = true },

        // 角色管理页(SysRoleController:CRUD + 授菜单 + 配数据范围 + 授用户)。
        // 三个授权抽屉各自是一颗按钮:回显(GET)与提交(PUT)一起授,授权菜单抽屉还要读菜单树。
        new SysMenu { Id = 240, ParentId = 200, Type = MenuType.Menu, Title = "角色管理", Permission = "", Path = "/system/role", Component = "system/role/index", Icon = "ph:shield-check-duotone", Sort = 4, Enabled = true, Visible = true },
        new SysMenu { Id = 241, ParentId = 240, Type = MenuType.Button, Title = "角色-查询", Permission = Codes("GET:/api/v1/sys/role/page", "GET:/api/v1/sys/role/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 242, ParentId = 240, Type = MenuType.Button, Title = "角色-新增", Permission = "POST:/api/v1/sys/role/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 243, ParentId = 240, Type = MenuType.Button, Title = "角色-更新", Permission = "PUT:/api/v1/sys/role/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 244, ParentId = 240, Type = MenuType.Button, Title = "角色-删除", Permission = Codes("DELETE:/api/v1/sys/role/{id}", "POST:/api/v1/sys/role/batch-delete"), Sort = 4, Enabled = true },
        new SysMenu { Id = 245, ParentId = 240, Type = MenuType.Button, Title = "角色-授权菜单", Permission = Codes("GET:/api/v1/sys/menu/tree", "GET:/api/v1/sys/role/{id}/menus", "PUT:/api/v1/sys/role/menu"), Sort = 5, Enabled = true },
        new SysMenu { Id = 246, ParentId = 240, Type = MenuType.Button, Title = "角色-数据范围", Permission = Codes("GET:/api/v1/sys/role/{id}/datascope", "PUT:/api/v1/sys/role/datascope"), Sort = 6, Enabled = true },
        new SysMenu { Id = 247, ParentId = 240, Type = MenuType.Button, Title = "角色-授权用户", Permission = Codes("GET:/api/v1/sys/role/{id}/users", "PUT:/api/v1/sys/role/users"), Sort = 7, Enabled = true },

        // ═══ 3xx 系统运维 ═══════════════════════════════════════════
        new SysMenu { Id = 300, ParentId = 0, Type = MenuType.Catalog, Title = "系统运维", Permission = "", Icon = "ph:wrench-duotone", Sort = 2, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },
        // 权限码锚点(无页面):探针。挂靠系统运维目录,仅承载权限码。
        new SysMenu { Id = 301, ParentId = 300, Type = MenuType.Button, Title = "连通性探针", Permission = "GET:/api/v1/ping", Sort = 99, Enabled = true },

        // 系统配置页(ConfigController CRUD + 分类 Tab 批量存值 + 第三方登录 Tab + 可选安全 Tab)。
        // 查询含按键取值与第三方登录方式清单(均为本页 Tab 的只读数据源);更新含 Tab 表单的批量存值。
        new SysMenu { Id = 310, ParentId = 300, Type = MenuType.Menu, Title = "系统配置", Permission = "", Path = "/system/config", Component = "system/config/index", Icon = "ph:sliders-horizontal-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 311, ParentId = 310, Type = MenuType.Button, Title = "配置-查询", Permission = Codes("GET:/api/v1/sys/config/page", "GET:/api/v1/sys/config/{id}", "GET:/api/v1/sys/config/value/{key}", "GET:/api/v1/auth/external/providers/all"), Sort = 1, Enabled = true },
        new SysMenu { Id = 312, ParentId = 310, Type = MenuType.Button, Title = "配置-新增", Permission = "POST:/api/v1/sys/config", Sort = 2, Enabled = true },
        new SysMenu { Id = 313, ParentId = 310, Type = MenuType.Button, Title = "配置-更新", Permission = Codes("PUT:/api/v1/sys/config/{id}", "PUT:/api/v1/sys/config/batch"), Sort = 3, Enabled = true },
        new SysMenu { Id = 314, ParentId = 310, Type = MenuType.Button, Title = "配置-删除", Permission = "DELETE:/api/v1/sys/config/{id}", Sort = 4, Enabled = true },
        // 可选安全:高敏权限清单 + MFA 清除(ADR 0006 非测评产品,默认启用但不授给默认角色)。
        new SysMenu { Id = 315, ParentId = 310, Type = MenuType.Button, Title = "高敏权限-查询", Permission = "GET:/api/v1/sys/mfa/high-sensitivity", Sort = 5, Enabled = true },
        new SysMenu { Id = 316, ParentId = 310, Type = MenuType.Button, Title = "高敏权限-新增", Permission = "POST:/api/v1/sys/mfa/high-sensitivity", Sort = 6, Enabled = true },
        new SysMenu { Id = 317, ParentId = 310, Type = MenuType.Button, Title = "高敏权限-删除", Permission = "DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}", Sort = 7, Enabled = true },
        new SysMenu { Id = 318, ParentId = 310, Type = MenuType.Button, Title = "MFA-清除用户二因子", Permission = "POST:/api/v1/sys/mfa/clear", Sort = 8, Enabled = true },

        // 字典管理页(DictController 主从 CRUD)。items/{typeCode} 走 [ActiveSession](任何登录用户可读),不占权限码。
        new SysMenu { Id = 320, ParentId = 300, Type = MenuType.Menu, Title = "字典管理", Permission = "", Path = "/system/dict", Component = "system/dict/index", Icon = "ph:book-open-text-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 321, ParentId = 320, Type = MenuType.Button, Title = "字典类型-查询", Permission = Codes("GET:/api/v1/sys/dict/type/page", "GET:/api/v1/sys/dict/type/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 322, ParentId = 320, Type = MenuType.Button, Title = "字典类型-新增", Permission = "POST:/api/v1/sys/dict/type", Sort = 2, Enabled = true },
        new SysMenu { Id = 323, ParentId = 320, Type = MenuType.Button, Title = "字典类型-更新", Permission = "PUT:/api/v1/sys/dict/type/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 324, ParentId = 320, Type = MenuType.Button, Title = "字典类型-删除", Permission = Codes("DELETE:/api/v1/sys/dict/type/{id}", "POST:/api/v1/sys/dict/type/batch-delete"), Sort = 4, Enabled = true },
        new SysMenu { Id = 325, ParentId = 320, Type = MenuType.Button, Title = "字典项-查询", Permission = "GET:/api/v1/sys/dict/item/page", Sort = 5, Enabled = true },
        new SysMenu { Id = 326, ParentId = 320, Type = MenuType.Button, Title = "字典项-新增", Permission = "POST:/api/v1/sys/dict/item", Sort = 6, Enabled = true },
        new SysMenu { Id = 327, ParentId = 320, Type = MenuType.Button, Title = "字典项-更新", Permission = "PUT:/api/v1/sys/dict/item/{id}", Sort = 7, Enabled = true },
        new SysMenu { Id = 328, ParentId = 320, Type = MenuType.Button, Title = "字典项-删除", Permission = Codes("DELETE:/api/v1/sys/dict/item/{id}", "POST:/api/v1/sys/dict/item/batch-delete"), Sort = 8, Enabled = true },

        // 菜单管理页(MenuController CRUD)。查询含路由清单——菜单表单里"权限码"下拉的数据源。
        new SysMenu { Id = 330, ParentId = 300, Type = MenuType.Menu, Title = "菜单管理", Permission = "", Path = "/system/menu", Component = "system/menu/index", Icon = "ph:list-dashes-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 331, ParentId = 330, Type = MenuType.Button, Title = "菜单-查询", Permission = Codes("GET:/api/v1/sys/menu/tree", "GET:/api/v1/sys/menu/routes"), Sort = 1, Enabled = true },
        new SysMenu { Id = 332, ParentId = 330, Type = MenuType.Button, Title = "菜单-新增", Permission = "POST:/api/v1/sys/menu/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 333, ParentId = 330, Type = MenuType.Button, Title = "菜单-更新", Permission = "PUT:/api/v1/sys/menu/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 334, ParentId = 330, Type = MenuType.Button, Title = "菜单-删除", Permission = "DELETE:/api/v1/sys/menu/{id}", Sort = 4, Enabled = true },

        // 模块管理页(ModuleController CRUD)。
        new SysMenu { Id = 340, ParentId = 300, Type = MenuType.Menu, Title = "模块管理", Permission = "", Path = "/system/module", Component = "system/module/index", Icon = "ph:squares-four-duotone", Sort = 4, Enabled = true, Visible = true },
        new SysMenu { Id = 341, ParentId = 340, Type = MenuType.Button, Title = "模块-查询", Permission = Codes("GET:/api/v1/sys/module/list", "GET:/api/v1/sys/module/{id}"), Sort = 1, Enabled = true },
        new SysMenu { Id = 342, ParentId = 340, Type = MenuType.Button, Title = "模块-新增", Permission = "POST:/api/v1/sys/module/add", Sort = 2, Enabled = true },
        new SysMenu { Id = 343, ParentId = 340, Type = MenuType.Button, Title = "模块-更新", Permission = "PUT:/api/v1/sys/module/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 344, ParentId = 340, Type = MenuType.Button, Title = "模块-删除", Permission = "DELETE:/api/v1/sys/module/{id}", Sort = 4, Enabled = true },

        // 消息通知页(NoticeController 管理端)。用户端(我的/未读数/标记已读)走 [ActiveSession],不设按钮。
        new SysMenu { Id = 350, ParentId = 300, Type = MenuType.Menu, Title = "消息通知", Permission = "", Path = "/system/notice", Component = "system/notice/index", Icon = "ph:bell-duotone", Sort = 5, Enabled = true, Visible = true },
        new SysMenu { Id = 351, ParentId = 350, Type = MenuType.Button, Title = "通知-查询", Permission = "GET:/api/v1/sys/notice/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 352, ParentId = 350, Type = MenuType.Button, Title = "通知-发布", Permission = "POST:/api/v1/sys/notice", Sort = 2, Enabled = true },
        new SysMenu { Id = 354, ParentId = 350, Type = MenuType.Button, Title = "通知-删除", Permission = "DELETE:/api/v1/sys/notice/{id}", Sort = 3, Enabled = true },

        // 回收站页(全局软删数据恢复/彻底删除)。
        new SysMenu { Id = 360, ParentId = 300, Type = MenuType.Menu, Title = "回收站", Permission = "", Path = "/system/recycle", Component = "system/recycle/index", Icon = "ph:trash-duotone", Sort = 6, Enabled = true, Visible = true },
        new SysMenu { Id = 361, ParentId = 360, Type = MenuType.Button, Title = "回收站-查询", Permission = "GET:/api/v1/sys/recycle/{type}/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 364, ParentId = 360, Type = MenuType.Button, Title = "回收站-彻底删除", Permission = "DELETE:/api/v1/sys/recycle/{type}/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 365, ParentId = 360, Type = MenuType.Button, Title = "回收站-恢复", Permission = "POST:/api/v1/sys/recycle/{type}/{id}/restore", Sort = 2, Enabled = true },

        // 服务器监控页(MonitorController 只读快照)。
        new SysMenu { Id = 370, ParentId = 300, Type = MenuType.Menu, Title = "服务器监控", Permission = "", Path = "/system/monitor", Component = "system/monitor/index", Icon = "ph:pulse-duotone", Sort = 7, Enabled = true, Visible = true },
        new SysMenu { Id = 371, ParentId = 370, Type = MenuType.Button, Title = "服务器监控-查询", Permission = "GET:/api/v1/sys/monitor/server", Sort = 1, Enabled = true },

        // 缓存管理页(CacheController 定向失效动作)。只清不看(缓存含明文 OTP、键含 PII,故无键浏览端点)。
        new SysMenu { Id = 380, ParentId = 300, Type = MenuType.Menu, Title = "缓存管理", Permission = "", Path = "/system/cache", Component = "system/cache/index", Icon = "ph:database-duotone", Sort = 8, Enabled = true, Visible = true },
        new SysMenu { Id = 385, ParentId = 380, Type = MenuType.Button, Title = "缓存-清授权", Permission = "POST:/api/v1/sys/cache/flush-auth", Sort = 1, Enabled = true },
        new SysMenu { Id = 386, ParentId = 380, Type = MenuType.Button, Title = "缓存-清字典", Permission = "POST:/api/v1/sys/cache/flush-dict", Sort = 2, Enabled = true },
        new SysMenu { Id = 387, ParentId = 380, Type = MenuType.Button, Title = "缓存-清配置", Permission = "POST:/api/v1/sys/cache/flush-config", Sort = 3, Enabled = true },
        new SysMenu { Id = 388, ParentId = 380, Type = MenuType.Button, Title = "缓存-重建门户菜单", Permission = "POST:/api/v1/sys/cache/rebuild-portal", Sort = 4, Enabled = true },

        // ═══ 4xx 任务调度 ═══════════════════════════════════════════
        // 目录名用「任务调度」而非「定时任务」——后者与子菜单重名,菜单树里读着像套娃。
        new SysMenu { Id = 400, ParentId = 0, Type = MenuType.Catalog, Title = "任务调度", Permission = "", Icon = "ph:timer-duotone", Sort = 3, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 定时任务页(JobController)。查询含处理器清单(表单下拉数据源);preview-cron 走 [ActiveSession],不占权限码。
        new SysMenu { Id = 410, ParentId = 400, Type = MenuType.Menu, Title = "定时任务", Permission = "", Path = "/system/job", Component = "system/job/index", Icon = "ph:clock-countdown-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 411, ParentId = 410, Type = MenuType.Button, Title = "任务-查询", Permission = Codes("GET:/api/v1/sys/job/page", "GET:/api/v1/sys/job/handlers"), Sort = 1, Enabled = true },
        new SysMenu { Id = 412, ParentId = 410, Type = MenuType.Button, Title = "任务-新增", Permission = "POST:/api/v1/sys/job", Sort = 2, Enabled = true },
        new SysMenu { Id = 413, ParentId = 410, Type = MenuType.Button, Title = "任务-更新", Permission = "PUT:/api/v1/sys/job/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 414, ParentId = 410, Type = MenuType.Button, Title = "任务-删除", Permission = Codes("DELETE:/api/v1/sys/job/{id}", "POST:/api/v1/sys/job/batch-delete"), Sort = 4, Enabled = true },
        new SysMenu { Id = 415, ParentId = 410, Type = MenuType.Button, Title = "任务-启停", Permission = "PUT:/api/v1/sys/job/{id}/enabled", Sort = 5, Enabled = true },
        new SysMenu { Id = 416, ParentId = 410, Type = MenuType.Button, Title = "任务-执行一次", Permission = "POST:/api/v1/sys/job/{id}/run", Sort = 6, Enabled = true },

        new SysMenu { Id = 420, ParentId = 400, Type = MenuType.Menu, Title = "执行记录", Permission = "", Path = "/system/job-log", Component = "system/job-log/index", Icon = "ph:list-checks-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 421, ParentId = 420, Type = MenuType.Button, Title = "记录-查询", Permission = "GET:/api/v1/sys/job/log/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 425, ParentId = 420, Type = MenuType.Button, Title = "记录-终止", Permission = "POST:/api/v1/sys/job/log/{id}/kill", Sort = 2, Enabled = true },
        new SysMenu { Id = 426, ParentId = 420, Type = MenuType.Button, Title = "记录-清空", Permission = "POST:/api/v1/sys/job/log/clear", Sort = 3, Enabled = true },

        new SysMenu { Id = 430, ParentId = 400, Type = MenuType.Menu, Title = "任务监控", Permission = "", Path = "/system/job-monitor", Component = "system/job-monitor/index", Icon = "ph:gauge-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 431, ParentId = 430, Type = MenuType.Button, Title = "监控-查询", Permission = "GET:/api/v1/sys/job/dashboard", Sort = 1, Enabled = true },

        // ═══ 5xx 日志审计 ═══════════════════════════════════════════
        new SysMenu { Id = 500, ParentId = 0, Type = MenuType.Catalog, Title = "日志审计", Permission = "", Icon = "ph:clipboard-text-duotone", Sort = 4, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 登录日志页(SysLogController 只读 + 清空)。
        new SysMenu { Id = 510, ParentId = 500, Type = MenuType.Menu, Title = "登录日志", Permission = "", Path = "/system/log/login", Component = "system/log/login/index", Icon = "ph:sign-in-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 511, ParentId = 510, Type = MenuType.Button, Title = "登录日志-查询", Permission = "GET:/api/v1/sys/log/login/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 515, ParentId = 510, Type = MenuType.Button, Title = "登录日志-清空", Permission = "DELETE:/api/v1/sys/log/login", Sort = 2, Enabled = true },

        // 操作日志页(SysLogController 只读 + 清空 + 导出)。
        new SysMenu { Id = 520, ParentId = 500, Type = MenuType.Menu, Title = "操作日志", Permission = "", Path = "/system/log/op", Component = "system/log/op/index", Icon = "ph:scroll-duotone", Sort = 2, Enabled = true, Visible = true },
        new SysMenu { Id = 521, ParentId = 520, Type = MenuType.Button, Title = "操作日志-查询", Permission = "GET:/api/v1/sys/log/op/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 525, ParentId = 520, Type = MenuType.Button, Title = "操作日志-清空", Permission = "DELETE:/api/v1/sys/log/op", Sort = 2, Enabled = true },
        new SysMenu { Id = 526, ParentId = 520, Type = MenuType.Button, Title = "操作日志-导出", Permission = "GET:/api/v1/sys/log/op/export", Sort = 3, Enabled = true },

        // 异常日志页(SysLogController 只读 + 清空,记未捕获异常)。
        new SysMenu { Id = 530, ParentId = 500, Type = MenuType.Menu, Title = "异常日志", Permission = "", Path = "/system/log/exception", Component = "system/log/exception/index", Icon = "ph:warning-octagon-duotone", Sort = 3, Enabled = true, Visible = true },
        new SysMenu { Id = 531, ParentId = 530, Type = MenuType.Button, Title = "异常日志-查询", Permission = "GET:/api/v1/sys/log/exception/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 535, ParentId = 530, Type = MenuType.Button, Title = "异常日志-清空", Permission = "DELETE:/api/v1/sys/log/exception", Sort = 2, Enabled = true },

        // 在线会话页(SysSessionController 只读 + 强退)。
        new SysMenu { Id = 540, ParentId = 500, Type = MenuType.Menu, Title = "在线会话", Permission = "", Path = "/system/session", Component = "system/session/index", Icon = "ph:broadcast-duotone", Sort = 4, Enabled = true, Visible = true },
        new SysMenu { Id = 541, ParentId = 540, Type = MenuType.Button, Title = "在线会话-查询", Permission = "GET:/api/v1/sys/session/online", Sort = 1, Enabled = true },
        new SysMenu { Id = 545, ParentId = 540, Type = MenuType.Button, Title = "强制下线", Permission = "DELETE:/api/v1/sys/session/{sessionid}", Sort = 2, Enabled = true },

        // ═══ 6xx 文件管理 ═══════════════════════════════════════════
        new SysMenu { Id = 600, ParentId = 0, Type = MenuType.Catalog, Title = "文件管理", Permission = "", Icon = "ph:folder-duotone", Sort = 5, Enabled = true, ModuleId = DefaultModuleSeed.BUILTIN_MODULE_ID },

        // 文件管理页(SysFileController)。上传含分片三步(init 秒传探测 + chunk 传片 + complete 合并落库)。
        new SysMenu { Id = 610, ParentId = 600, Type = MenuType.Menu, Title = "文件管理", Permission = "", Path = "/system/file", Component = "system/file/index", Icon = "ph:files-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 611, ParentId = 610, Type = MenuType.Button, Title = "文件-查询", Permission = "GET:/api/v1/sys/file/page", Sort = 1, Enabled = true },
        new SysMenu { Id = 612, ParentId = 610, Type = MenuType.Button, Title = "文件-上传", Permission = Codes("POST:/api/v1/sys/file/upload", "POST:/api/v1/sys/file/chunk/init", "POST:/api/v1/sys/file/chunk", "POST:/api/v1/sys/file/chunk/complete"), Sort = 2, Enabled = true },
        new SysMenu { Id = 614, ParentId = 610, Type = MenuType.Button, Title = "文件-删除", Permission = Codes("DELETE:/api/v1/sys/file/{id}", "POST:/api/v1/sys/file/batch-delete"), Sort = 4, Enabled = true },
        new SysMenu { Id = 615, ParentId = 610, Type = MenuType.Button, Title = "文件-下载", Permission = "GET:/api/v1/sys/file/{id}/download", Sort = 3, Enabled = true },
    ];
}
