# 权限设计指导（消费端）

面向装了 SmartAdmin 包、在它上面长业务模块的宿主工程。讲的是权限该怎么切、菜单种子该怎么写、前端按钮该怎么门控，以及一套现成系统怎么对齐到这个标准。内核侧的实现细节只在需要判断时点到，不展开。

## 一、模型

授权单元是**菜单按钮**，执行单元是**路由**，一对多。

- 权限码就是规范化路由：`{大写 METHOD}:/{小写路由模板}`，如 `GET:/api/v1/sys/user/page`。代码里不出现任何权限字符串，控制器动作只挂无参的 `[RolePermission]`。
- 一颗按钮的 `Permission` 字段可以挂多条码，以 `;` 连接。角色勾上这颗按钮，这些路由一起授出。
- 后端授权仍逐条路由比对：`[RolePermission]` 拿请求路由算出的码，看它在不在用户的权限码集合里。集合是所有已授按钮的码展开后的并集。
- 拆合规则只有一处：`PermissionCode.Split` / `PermissionCode.Join`（`SmartAdmin.Core`）。按 `;` 拆、去空白、逐条规范化（Method 大写、路由小写）、去重。菜单保存与权限聚合都经它，所以手敲的大小写差异到不了库里。

管理员在角色页看到的是能力（「用户-查询」），不是接口清单。接口是能力的实现细节，可以增减而不影响授权界面。

## 二、每个页面固定四颗按钮

| 按钮 | Id 个位 | 归进来的路由 |
|---|---|---|
| X-查询 | 1 | 列表、详情，以及该页表单要用的只读数据源（如处理器清单、路由清单） |
| X-新增 | 2 | 新增 |
| X-更新 | 3 | 更新、批量存值 |
| X-删除 | 4 | 删除、批量删除 |

页面独有的操作各加一颗，Id 个位取 5–9，标题用页面自己的动词：`用户-重置密码`、`用户-启停`、`任务-执行一次`、`机构-复制`、`文件-下载`。判据是**管理员会不会想单独授或单独收**。会，就独立一颗；不会，并进四颗里。

几个定型的归法：

- 导入向导的预览、重验、错误报告、提交四步是一颗「X-导入」。勾了预览没勾提交是半截权限，没有场景需要它。
- 导出单独一颗「X-导出」。导出常常受合规限制，管理员会单独收。
- 分片上传的初始化、传片、合并三步并进「X-上传」。
- 角色页的三个授权抽屉各一颗：`角色-授权菜单`（读菜单树 + 取已授菜单 + 提交）、`角色-数据范围`（取 + 提交）、`角色-授权用户`（取 + 提交）。回显和提交拆开授没有意义。

标题格式固定 `页面名-动词`。角色授权界面按页面分组展示按钮，前缀让同名动词在不同组里不混。

## 三、哪些端点不进按钮

挂 `[ActiveSession]` 的端点任何登录用户都能调，不占权限码、不建按钮：

- 个人中心：看改自己的资料、改密、绑定外部账号。
- 表单里人人要用的只读数据源：字典项 `dict/items/{typeCode}`、密码策略、cron 预览。
- 导入模板下载。
- 首页工作台统计。

判据是**这个数据本身不越权**。数据范围过滤器对 `[ActiveSession]` 端点同样生效，所以「任何登录用户可调」不等于「看到全部数据」。

跨页面的下拉数据源按这条处理。用户表单要选角色、岗位、机构，不要靠给用户管理员顺手勾上角色、岗位、机构三页的查询：给这类下拉单开一个 `[ActiveSession]` 的 `options` 端点，只回 `id` 与 `name`，完整的列表与单条接口仍留在各自页面的查询按钮后面。

## 四、菜单种子

消费方自己的模块用自己的种子类播菜单，按钮与页面都是 `SysMenu` 行。

```csharp
public class DeviceMenuSeed : ISeedData<SysMenu>
{
    public bool SyncOnUpgrade => true;
    public string[]? SyncColumns =>
    [
        nameof(SysMenu.ParentId), nameof(SysMenu.Type), nameof(SysMenu.Permission),
        nameof(SysMenu.Path), nameof(SysMenu.Component), nameof(SysMenu.Icon), nameof(SysMenu.ModuleId),
    ];

    public IEnumerable<SysMenu> HasData() =>
    [
        new SysMenu { Id = 1010, ParentId = 1000, Type = MenuType.Menu, Title = "设备管理", Permission = "", Path = "/biz/device", Component = "biz/device/index", Icon = "ph:cpu-duotone", Sort = 1, Enabled = true, Visible = true },
        new SysMenu { Id = 1011, ParentId = 1010, Type = MenuType.Button, Title = "设备-查询", Permission = PermissionCode.Join(["GET:/api/v1/biz/device/page", "GET:/api/v1/biz/device/{id}"]), Sort = 1, Enabled = true },
        new SysMenu { Id = 1012, ParentId = 1010, Type = MenuType.Button, Title = "设备-新增", Permission = "POST:/api/v1/biz/device", Sort = 2, Enabled = true },
        new SysMenu { Id = 1013, ParentId = 1010, Type = MenuType.Button, Title = "设备-更新", Permission = "PUT:/api/v1/biz/device/{id}", Sort = 3, Enabled = true },
        new SysMenu { Id = 1014, ParentId = 1010, Type = MenuType.Button, Title = "设备-删除", Permission = PermissionCode.Join(["DELETE:/api/v1/biz/device/{id}", "POST:/api/v1/biz/device/batch-delete"]), Sort = 4, Enabled = true },
    ];
}
```

要点：

- Id 从 `SmartSeedIds.ConsumerMin`（1000）起，`[1, 999]` 归内核。同实体内 Id 不得重复，启动检查会拒绝。
- 编号照内核的规则：每个目录占一个百位段（1000、1100……），整百是目录，页面取整十，按钮个位 1 查询、2 新增、3 更新、4 删除，5–9 放独有操作，缺的标准按钮空着位。上面的 1010–1014 就是这么排的。
- 只给移动端、PDA、第三方系统调的接口没有页面：建一个目录当权限组，`Path` 与 `Component` 留空，按钮直接挂目录下。角色授权界面把它们渲染成该目录组里「接口权限（无页面）」一行。
- `SyncOnUpgrade` 加 `SyncColumns` 让结构列随发版刷到老库，标题、排序、可见、启用留给用户。改了已有行的结构列要 bump 宿主自己的版本闸门；内核的 `SysSchemaVersion.Current` 只管内核种子。
- 每个 `[RolePermission]` 端点都应出现在某颗按钮里，否则普通用户对它静默 403。内核用 `PermissionCodeConsistencyTests` 反射控制器与种子双向比对，宿主工程照这个思路给自己的控制器与种子写一条。

## 五、前端

- `v-auth` 与 `authStore.hasPerm` 只认单条路由码：`v-auth="'POST:/api/v1/biz/device'"`。按钮挂了几条码与门控无关，`/personal/permissions` 返回的已经是展开后的单条码集合。
- 编辑弹窗回显调的是详情接口，它归「查询」。不会出现列表能看、编辑弹不开的半截权限。
- 菜单管理页的按钮编辑器里，权限码是多选框，选项来自 `GET /sys/menu/routes`（后端实时算出的受权路由清单，含宿主工程自己的控制器）。多选的结果存成 `;` 连接的一条串，类型层的 `splitPermission` / `joinPermission` 负责拆合。
- 操作列的行内按钮在 `render` 里用 `authStore.hasPerm(code)` 判，与指令同一套规则。

## 六、合并已有按钮

一个跑着的系统把按钮从「一条路由一颗」收拢成「一个能力一颗」，要处理两件事：存活按钮的 `Permission` 补上并进来的路由，角色对旧按钮的授权挪到存活按钮。

做法是一张「旧 Id → 存活 Id」的表加一个 `IDatabaseReadyHook`：每次启动探一次旧 Id，库里还有就把角色关联挪到存活节点（已授过的不重复插）、删旧节点的关联行与本体：

```csharp
public class DeviceMenuMergeHook(ISqlSugarClient db) : IDatabaseReadyHook
{
    // 旧 Id → 存活 Id;旧 Id 永久保留不复用
    private static readonly Dictionary<long, long> MergedInto = new() { [1015] = 1011 };

    public async Task OnDatabaseReadyAsync(DatabaseReadyContext context, CancellationToken ct)
    {
        if (!context.SeedRan) return;
        foreach (var (oldId, survivor) in MergedInto)
        {
            if (!await db.Queryable<SysMenu>().ClearFilter().AnyAsync(m => m.Id == oldId)) continue;
            await db.RunInTransactionAsync(async () =>
            {
                var roleIds = await db.Queryable<SysRoleMenu>().Where(x => x.MenuId == oldId).Select(x => x.RoleId).ToListAsync();
                var granted = await db.Queryable<SysRoleMenu>().Where(x => x.MenuId == survivor && roleIds.Contains(x.RoleId)).Select(x => x.RoleId).ToListAsync();
                var links = roleIds.Except(granted).Select(r => new SysRoleMenu { RoleId = r, MenuId = survivor }).ToList();
                if (links.Count > 0) await db.Insertable(links).ExecuteCommandAsync();
                await db.Deleteable<SysRoleMenu>().Where(x => x.MenuId == oldId).ExecuteCommandAsync();
                await db.Deleteable<SysMenu>().Where(m => m.Id == oldId).ExecuteCommandAsync();
            });
        }
    }
}
```

注册：`services.TryAddEnumerable(ServiceDescriptor.Transient<IDatabaseReadyHook, DeviceMenuMergeHook>())`。存活按钮的新 `Permission` 由种子的 `SyncOnUpgrade` 刷到库里，钩子只管关联迁移。合并后角色能调的接口只增不减。

## 七、把一套现有系统对齐到这个标准

按顺序做，每步都能单独验证。

1. **重建内核菜单，再升内核包。** 内核菜单按百位分区编号（见 `DefaultMenuSeed` 头注释），老库里的内核菜单行与角色授权不做迁移。升级前删掉 `sys_menu` 里 Id ≤ 999 的行和它们在 `sys_role_menu` 里的关联，启动后种子按新编号重播，再给角色重新勾内核菜单。自己挂在内核目录下的节点、代码或种子里写死的内核菜单 Id，一并换成新编号。用 Redis 缓存的话，到缓存管理页点一次「清授权」。
2. **梳理自己的菜单种子。** 每个页面收成四颗加独有操作，查询挂列表 + 详情，删除含批量删除。多条码用 `PermissionCode.Join`。种子里被并掉的旧 Id 登记到自己的合并表，不复用。
3. **写自己的合并钩子。** 形状见第六节。角色种子若引用了被并掉的菜单 Id，改成存活 Id。
4. **补 `options` 端点。** 跨页下拉数据源挂 `[ActiveSession]`，前端下拉改调它，各页的查询按钮不再互相依赖。
5. **前端升级。** 把 `smart-admin-web` 升到与内核 NuGet 包同一个版本，菜单管理页的多码按钮编辑随包到位；`v-auth` 不用动。
6. **验证。** 建一个只授「X-查询」的角色，用它登录：列表 200、详情 200、更新 403、别的页面 403。菜单管理页打开一颗按钮，多条码以 tag 逐条显示。控制器与种子的双向一致性测试跑绿。

## 八、检查清单

- [ ] 每个页面恰好四颗基础按钮，标题 `页面名-查询/新增/更新/删除`。
- [ ] 查询按钮含列表与详情两条路由；有编辑弹窗回显的页面不会因缺详情而 403。
- [ ] 批量删除在删除按钮里，批量存值在更新按钮里，导入四步在一颗按钮里。
- [ ] 每个 `[RolePermission]` 端点出现在某颗按钮里，每颗按钮的码都对应真实端点。
- [ ] 跨页下拉走 `[ActiveSession]` 的 `options` 端点。
- [ ] 消费方种子 Id ≥ 1000，按百位分区、整十页面、个位 1–4 固定排；被并掉的 Id 登记在合并表里且不复用。
- [ ] `v-auth` 与行内 `hasPerm` 写的是单条路由码。
