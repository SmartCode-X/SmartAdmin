# SmartAdmin 代码规范

> 面向后续开发的落地规范。规则均从现有代码提炼，每条尽量给出参照文件，照抄即合规。
> 配套文档：新建业务见 [`new-business-guide.md`](./new-business-guide.md)。

---

## 0. 总则

1. **可替换性优先**：内核以 NuGet 分发，消费方不改源码即可替换任一部件。凡新增可替换服务，一律 `TryAdd*` 注册、接口背书、方法拆 `virtual` 步。这是硬约束，不是建议。
2. **注释讲“为什么”**：解释边界、权衡、坑，而非复述代码。公共类型/成员必须有 XML 文档注释。注释只写现状，不写变更史、任务号、issue 号、设计文档节号；要说设计取舍的理由，把理由本身写出来。
3. **中文注释**：代码、注释、文档统一中文，与既有代码一致。
4. **错误是数字码**：后端永不返回本地化文案，只返回 `ErrorCode`；文案在前端按码翻译。
5. **刻意简化留痕**：临时/有上限的简化用 `// ponytail:` 注释标注上限与升级路径（例：`ConfigService.SaveValuesAsync` 上的「少量键逐条查改足够；键集变大再合并成 IN 查询 + 批量更新」）。

---

## 1. 后端规范（.NET 10 内核）

### 1.1 分层与依赖方向

依赖只能向下，越层禁止。新增代码先想清楚落在哪层：

```
Core        契约层：接口(I*Provider/I*Service 面)、Options、Result<T>、ErrorCode、AdminException。无 SqlSugar、无 ASP.NET。
  ↑
SqlSugar    数据层：ISqlSugarClient 单例、IRepository<>、实体基类、CodeFirst 初始化、种子运行器。
  ↑
Services    领域层：实体(Sys*)、*Service 实现、RBAC/数据范围 Provider、事件总线。★实体放这里，不放 SqlSugar 层。
  ↑
AspNetCore  宿主集成：AddSmartAdmin/MapSmartAdmin、JWT、过滤器、内置控制器。
  ↑
SmartAdmin  元包：只引用 AspNetCore，消费方装它即拉全栈。
```

- 运行时依赖**仅** SqlSugarCore + Microsoft.\*，核心包不得引入其它第三方框架。
- 每层装配是一个 `*Setup.cs` 扩展方法：`SqlSugarSetup` → `ServicesSetup` → `SmartAdminSetup`（组合根，`AddSmartAdmin` 逐层向下调）。

### 1.2 可替换性契约（`ReplaceabilityTests` 锁定）

| 手段 | 做法 | 参照 |
|---|---|---|
| **TryAdd 注册** | 内置服务全部 `TryAdd*`；消费方在 `AddSmartAdmin()` 之前注册同接口即胜出。**严禁**对可替换服务用裸 `Add*`。 | `ServicesSetup.cs`、`SqlSugarSetup.cs` |
| **virtual 模板方法** | 长方法拆成小 `virtual` 步，消费方覆写一步而非抄整方法。 | `SessionService.EnforceConcurrencyAsync`、`RbacPermissionProvider.LoadFromDatabaseAsync` |
| **接口背书** | 每个服务先有 `I*Service`，实现类 `virtual`。 | 全部 `Services/*/I*.cs` |
| **消费方装配** | 业务程序集经 `options.ApplicationAssemblies` 并入：实体参与建表、控制器 `AddApplicationPart`。改实体扫描/控制器注册时**务必保留此路径**。 | `SmartAdminSetup.AddSmartAdmin`：实体程序集并入 `entityAssemblies` 传给 `AddSmartAdminSqlSugar`；控制器程序集逐个 `mvc.AddApplicationPart(assembly)` |

扩展点完整清单（哪些接口可替换、生命周期是什么）见 `backend/tests/SmartAdmin.Tests/ReplaceabilityContract.cs`；判红的回归测试是同目录的 `ReplaceabilityTests.cs`。

### 1.3 实体规范

- **位置**：实体定义在 `Services/Entities/`，不在 SqlSugar 层。命名 `Sys*`（系统内核表）。
- **基类**：
  - `BaseEntity`（`SqlSugar/Entities/BaseEntity.cs`）：主键 + 审计四件套（CreateTime/CreateUserId/UpdateTime/UpdateUserId）+ 软删 `IsDelete`。这些字段由 AOP 自动填，业务代码零感知。
  - `DataEntity`（带机构数据范围的业务表继承它，含 `CreateOrgId` 锚点）——需要按机构做数据隔离时用它。
- **SqlSugar 特性**：`[SugarTable("表名", TableDescription=…)]`、唯一索引 `[SugarIndex(..., IsUnique=true)]`、列 `[SugarColumn(Length=…, ColumnDescription=…, IsNullable=…)]`。参照 `Entities/SysDictType.cs`。
- **演进列（已有表加字段）**：数据库列必须可空——`IsNullable = true`；读侧定义存量 `NULL` 的默认语义。新增属性可用 `T?` 表达该语义，但不得仅为迁移而改变已发布公共属性的 CLR 类型；保留既有 `bool` / `DateTime` 时，锁定 ORM 的默认值物化和读侧回退。禁止对已发版实体新增无 DEFAULT 的 `NOT NULL` 列（MSSQL 非空表 `ADD` 会失败）。新表首建不受此限。见 `skills/create-entity.md`「已有表加列」、`CodeFirstNullableUpgradeTests`。
- **不可变约定写进注释**：如“Code 创建后不可变”，并在 Service 的 Update 里落实（不改该字段）。

### 1.4 服务规范

- 一服务一目录：`I{X}Service.cs` + `{X}Service.cs` + `{X}Models.cs`（DTO：`{X}Input`/`{X}PageInput`/`{X}Output`，用 `record`）。
- 实现类构造函数注入依赖（主构造函数语法），方法 `virtual`，`async` 后缀 `Async`。
- 分页统一 `PagedList<T>` + `.ToPagedListAsync(current, size)`（`SqlSugar/Paging/`）。
- 校验用 `AdminException.ThrowIf(条件, ErrorCode.X)`。

### 1.5 错误处理

- 业务错误抛 `AdminException(ErrorCode)` 或返回 `ErrorCode`；由 `AdminExceptionFilter` 统一转信封。
- **`ErrorCode` 是数字枚举，永不带本地化文案**（`Core/ErrorCode.cs`）。新增错误码往枚举里加，同时标注 `[MsgKey("error.<模块>.<语义>")]`（如 `error.dict.typeNotFound`）并补前端两份语言包；漏标会回退成 `error.code.{数值}` 原样弹给用户，且 `ErrorCodeLocaleConsistencyTests` 会让后端测试变红。
- 控制器可直接 `return dto`，`ResultEnvelopeFilter` 兜底包 `Result<T>`；内置控制器为了 OpenAPI 契约清晰，显式返回 `Result<T>.Ok(...)`。

### 1.6 控制器规范

参照 `Controllers/DictController.cs`：

```csharp
[ApiController]
[Route("api/v1/sys/dict")]
[Module("Dict")]                       // 可经 Api:DisabledModules 关停整模块
public class DictController(IDictService svc) : ControllerBase
{
    [HttpGet("type/page")]
    [RolePermission]                   // ★权限码 = 规范化路由，无字符串
    public async Task<Result<PagedList<SysDictType>>> PageTypes([FromQuery] DictTypePageInput input) =>
        Result<PagedList<SysDictType>>.Ok(await svc.PageTypesAsync(input));
}
```

- **`[RolePermission]` 无参**：权限码就是 `{METHOD}:/{路由模板}`（如 `GET:/api/v1/sys/dict/type/page`），在角色-菜单界面勾路由即配权。**代码里永远不写 `"sys:user:add"` 之类魔法串**（`Security/RolePermissionAttribute.cs`）。超管 `sadm` claim 直接放行。
- **`[ActiveSession]`**：任意已登录用户可访问、但无需特定权限的端点用它。
- **`[OperationLog(...)]`**：需要审计的写操作挂它，`OperationLogFilter` 记录。
- **`[Module("X")]`**：模块化开关，可被配置摘除。模块行本身另有两道删除守卫：下挂菜单的一律拒删（`ErrorCode.ModuleHasMenus`，自建模块同样适用），内置 system 模块按固定 Id 永久保护，禁用/删除均拒（`ErrorCode.ModuleProtected`）——见 `ModuleService`。
- 匿名端点显式 `[AllowAnonymous]`（登录/刷新/验证码）。默认拒绝：`MapControllers().RequireAuthorization()` 全局兜底，漏挂 `[RolePermission]` 也不会静默公开。

### 1.7 数据访问

- 注入 `IRepository<T>`；复杂查询走 `.AsQueryable()`，需逃生时走 `.Db`（如 `Db.Deleteable<>()` 物理删关联行、`Db.Ado.UseTranAsync`）。
- **全局过滤器**（`SqlSugarSetup` 的 `AttachHooks` 本地方法，业务代码无需重复写）：
  - 软删：`ISoftDelete` 实体自动 `IsDelete == false`。查已删数据显式 `.ClearFilter<ISoftDelete>()`。
  - **数据范围**（招牌能力）：`IOrgScoped`/`DataEntity` 按当前请求生效机构集过滤。
- **唯一性查重要带上软删行**：`.ClearFilter<ISoftDelete>().AnyAsync(...)`，否则撞库唯一索引抛原生 500（见 `DictService.AddTypeAsync`、`ConfigService.AddAsync`）。
- **多写操作包事务**：`Db.Ado.UseTranAsync`，失败整体回滚；**缓存失效放在事务提交之后**（`RbacService.ReplaceAsync`、`SessionService.OpenAsync`）。
- 审计字段（Id 雪花、CreateTime/User/Org、UpdateTime/User）由 AOP 自动填（同一 `AttachHooks` 方法内的 `client.Aop.DataExecuting` 钩子），业务只设业务字段。`CreateOrgId` 不填则机构维度数据范围对业务表恒 0 行——不要手动绕过 AOP。
- 雪花 `WorkerId`：显式配 `SmartAdmin:Id:WorkerId` 则固定用它；不配则启动时由 `WorkerIdLease` 用文件锁在本机抢号，同机多进程不会同号。**跨机器、跨容器必须各配不同值**。运行期读有效机器号只能注入 `AdminIdOptions`，别直接读 `SmartAdminOptions.Id`。
- `WorkerIdLeaseGuard` 在 `sys_worker_lease` 上再兜一道。节点名是 `{机器名}#{机器号}@{实例token}`：`@` 前是稳定身份，参与「这个号被谁占着」的判定；`@` 后每次启动都变，只用于续租与释放的条件写（防旧进程回魂续了新主的租约）。**同机同号且前任 pid 已不在就直接接管，不等 TTL**——判定若只比完整节点名，进程重启后连自己都认不出来，每次硬杀（停止调试、关控制台、容器重启后 pid 复用）都要干等一个 TTL 才能再起。只有「同机另一个还活着的进程」和「另一台机器」才抛。改这里务必同时看 `WorkerIdLeaseGuardTests`：身份（机器名/pid/存活判定）是构造参数注入的，测试靠它模拟第二个进程。
- **查询表达式里的布尔标记一律写成 `flag == true`，不写裸 `flag`**。裸布尔（尤其是闭包捕获的 C# 变量）会被翻成 SQL 里的裸字面量 `1`/`0`；SQLite 与 MySQL 拿它当真值，**SQL Server 的谓词上下文直接拒**（`An expression of non-boolean type specified in a context where a condition is expected`）。写成比较式才渲染成 `@p = 1`，四方言通吃。
  ```csharp
  // ❌ 三条腿绿,sqlserver 上该端点整个 500
  .WhereIF(scopeOrgIds != null, u => ... || (scope!.IncludeSelf && u.Id == selfId))
  // ✅
  .WhereIF(scopeOrgIds != null, u => ... || (includeSelf == true && u.Id == selfId))
  ```
  实体列同理（`e.IsDelete == false` 而非 `!e.IsDelete`）。现有两处范例：`SqlSugarSetup` 的全局数据范围过滤器、`UserService.BuildListQuery`。**这类缺陷本地与 CI 的 sqlite/mysql/postgres 三条腿都不会红**，只有 sqlserver 腿抓得到。

### 1.8 缓存规范（性能核心，务必遵守）

系统采用 **读穿透 / cache-aside + 显式失效** 模型，不是每次查库。新增热读路径按此模板：

```csharp
public virtual async Task<T> GetHotAsync(string k)
{
    var key = CacheKeys.Xxx(k);               // ①逻辑键集中定义
    var cached = await cache.GetAsync<T>(key); // ②命中即返回
    if (cached is not null) return cached;
    var v = await LoadFromDb(k);               // ③未命中查库
    var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
    await cache.SetAsync(key, v, ttl);         // ④回填（TTL 仅兜底，主靠显式失效）
    return v;
}
// 任何增删改后：await cache.RemoveAsync(CacheKeys.Xxx(k));  ⑤显式失效
```

- **键集中在 `Core/CacheKeys.cs`，禁散落魔法串**。前缀 `Cache:KeyPrefix`（默认 `smart:`）由 provider 统一追加。
- **缓存值的空集合 ≠ 未缓存**（`ICacheProvider.GetAsync` 返回 `default`），无权限用户也只查一次库。
- 变更时**既失效缓存也广播事件**（`DictService.InvalidateAsync` → `DictChangedEvent`），供跨节点失效/审计/推送订阅。
- 一次性票据用 `GetAndRemoveAsync`（验证码），并发计数用 `IncrementAsync`（登录失败）——`MemoryCacheProvider` 用进程内锁保原子，`RedisCacheProvider` 用原生 `GETDEL`/`INCR`+`EXPIRE`。
- 默认 `MemoryCacheProvider`（进程内）；多实例共享装 **`SmartAdmin.Caching.Redis`** 可选包（基于 StackExchange.Redis），**业务代码零改动**：

  ```csharp
  builder.Services.AddSmartAdminRedisCache(builder.Configuration); // ★须在 AddSmartAdmin 之前,赢 TryAdd
  builder.Services.AddSmartAdmin(builder.Configuration);
  ```
  ```jsonc
  "SmartAdmin": { "Cache": { "Provider": "Redis", "RedisConnectionString": "127.0.0.1:6379", "KeyPrefix": "smart:" } }
  ```
  `Provider≠Redis` 时 `AddSmartAdminRedisCache` 空操作(留 Memory 默认)。缓存落 Redis 后，现有“变更即 `RemoveAsync` 失效”天然跨实例生效。值走 System.Text.Json 序列化——新增缓存的类型须可序列化(record/POCO，参照 `DataScopeResult`)。

已缓存的热读：用户权限码、用户数据范围、会话活跃态、字典项、系统配置。参照 `RbacPermissionProvider`、`DataScopeProvider`、`SessionService`、`DictService`、`ConfigService`。

### 1.9 DI 装配

- 装配写进 `*Setup.cs` 扩展方法。内置服务**显式** `TryAdd`（不靠扫描，可预测、可替换）；种子用 `TryAddEnumerable`（按实现类型防重）。
- 无状态服务 `Singleton`（哈希、验证码生成器、文件存储、缓存 provider、事件总线）；按请求 `Scoped`（多数业务服务，与仓储一致）。
- 消费方业务程序集经 `options.ApplicationAssemblies` 并入（实体建表 + 控制器挂载）。

### 1.10 种子数据

- 实现 **`ISeedData<TEntity>`**（泛型版），`HasData()` 返回默认行（`Seed/DictSeed.cs`）。返回空集合合法（"库里已有就不播种"，见 `SuperAdminSeed`）。
- **固定 Id 保幂等**：种子只在缺失时补，不回改已存在行——界面上的改动不会被重启覆盖。
- **Id 必须落在保留区间**（`Core/SmartSeedIds.cs`）：内核 `[1, 999]`、消费者 `>= 1000`。上限不是写死的数字，而是启动时刻动态算出的雪花地板（`SnowflakeIdGenerator.CurrentFloor()`）——严格小于它就永远不会被此后真实产生的雪花号撞上。越界或 Id=0 启动即拒。内核新增种子行别越过 999——`SeedIdRangeTests` 会拦。
- 内置种子在 `ServicesSetup`/`SqlSugarSetup` 用 `TryAddEnumerable` 注册；**消费者在自己的 `Program.cs` 注册**（内核不扫描程序集找种子，忘注册＝静默不执行）。

### 1.11 命名 / 组织 / 其它

- 命名空间随目录；一类型一文件；`Sys*` 实体、`I*` 接口、`*Service`/`*Provider`/`*Filter`/`*Attribute` 后缀。
- 启用可空引用类型；`async` 方法带 `Async` 后缀并接受 `CancellationToken`（热路径）。
- 时间统一走注入的 `TimeProvider`（可测试），不用 `DateTime.Now` 裸调。

### 1.12 包管理

- 版本**集中管理**：增/改依赖在 `backend/Directory.Packages.props` 的 `<PackageVersion>`，**不在各 `.csproj` 写版本号**。
- 共享构建/NuGet 元数据在 `backend/Directory.Build.props`。

### 1.13 注释规范（后端注释率约 28%）

- **面向消费者的公共 API 必须有 XML 文档注释**：接口及其成员、`Options` 类型与属性（写明默认值）、消费者可覆写的 `virtual` 方法、扩展方法、实体基类与内置实体的类型级注释。实现类的成员用 `<inheritdoc/>` 继承接口文档，不重复写。DTO（`*Models.cs`）与属性名字自解释、已带 `ColumnDescription` 的实体属性可省。`Directory.Build.props` 关掉了 `CS1591`（缺 XML 注释不算编译警告），所以这条规范靠代码评审保证，编译器不拦。
- 行内注释解释 WHY（并发、事务顺序、边界、跨方言坑），不复述 WHAT；只写现状，不写任务号、issue 号、设计文档节号、被替换掉的历史方案名。
- 简化/有上限处用 `// ponytail:` 注明上限与升级路径。

---

## 2. 前端规范（Vue 3 + Naive UI）

### 2.1 技术栈与目录

`<script setup>` + Naive UI + Pinia(持久化) + vue-router + vue-i18n + VueUse。`web/` 是 npm workspace：`packages/admin` 是前端内核包 `smart-admin-web`（与 NuGet 包同号发布），`template` 是应用薄壳（消费者 degit 的起点，也是 e2e 的宿主）。包内路径别名 `#/` → `src/`；**包里不写 `@/`**，到了应用里 `@/` 指的是应用自己的 `src`。

包内目录（`web/packages/admin/src/`）：

| 目录 | 职责 |
|---|---|
| `views/` | 内置页面（按模块/实体分子目录，`views/<模块>/<实体>/index.vue`） |
| `composables/` | 与 UI 库无关的逻辑单源（`use*`），Naive 消息留在视图层 |
| `stores/` | Pinia 状态 |
| `layouts/` | 布局壳（顶栏/侧栏/标签/设置） |
| `components/` | 可复用组件 |
| `api/` | `client.ts`(openapi-fetch) + `index.ts`(按域分组) + 生成的 `schema.d.ts` |
| `router/` | 静态路由 + 动态路由重建 + 页面表 `viewRegistry.ts` |
| `theme/`、`styles/` | 主题令牌 |
| `locales/` | i18n |
| `directives/` | `v-auth` 等 |
| `types/` | 手写类型（`menu.ts`）与再导出 |
| `lib/` | 运行期配置 `runtime.ts`、图标注册等启动件 |
| `createSmartAdmin.ts` / `index.ts` | 初始化函数 / 包的公开 API 面 |

#### 包的公开 API 与扩展方式

- **公开 API 面就是 `src/index.ts`。** 应用只能 `import { X } from 'smart-admin-web'`，页面可能用到的东西都在那里具名导出，改导出签名即破坏性变更。`layouts/`、`views/`、`App.vue`、`setupIcons` 等是内部件，不导出。新增共享组件或 composable 时同步加导出。
- **peerDependencies 只放单例。** 持有全局单例的依赖（app 实例、`SMART_TABLE_DEFAULTS` 这类 injection key、图标注册表、pinia / router 实例）必须由应用装、只有一份：vue、vue-router、pinia、vue-i18n、naive-ui、@vueuse/core、@iconify/vue、smart-naive-table、smart-naive-icon。装成两份不报错，表现是表格拿不到注入的默认值、权限与路由无声失效。其余是普通 dependencies，库构建时同样外置。
- **应用经 `createSmartAdmin` 扩展，不改包。** 包是预编译的，包内的 `import.meta.glob` 看不见应用的文件，所以由应用写 glob、把结果交进来：

  | 应用的东西 | 放在应用的 | 怎么并入 |
  |---|---|---|
  | 页面 | `src/views/<模块>/` | `views: import.meta.glob('./views/**/*.vue')`；页面 key = `views/` 之后去掉 `.vue` 的路径，即菜单的 `component` 字段；与内置页同 key 即覆盖 |
  | i18n 文案 | `src/locales/ext/<locale>/<模块>.ts` | `locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true })`，按命名空间深合并 |
  | API 封装 | `src/api/<域>.ts` | 从 `smart-admin-web` 导入 `unwrap`/`ApiError`/`pageParams`/`toPage`；客户端用 `createApiClient<paths>()` |
  | 领域类型 | `src/types/<模块>.ts` | 普通 import |
  | 顶级静态路由（整屏看板、打印页） | 路由表 | `routes: [...]` |
  | 本地 SVG 图标 | `src/assets/svg/*.svg` | `icons: import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })` |
  | 菜单角标、顶栏工具、自己的 `app.use` | — | `install(app)` 里调 `registerMenuBadge` / `registerHeaderTool` 等 |

  一组可复用的扩展打成插件（`SmartAdminPlugin`，与上表同形），经 `plugins: [...]` 传入。覆盖顺序 内核 < 插件（按数组顺序）< 应用，同 key 时后注册者优先，所以应用永远压过内核。后端靠 `TryAdd` 得到的是同一个结果，只是那边是消费方先注册者胜出。要改内置页，要么把需求做进内核成配置项，要么整页复制到应用 `views/` 用同名 key 覆盖（复制来的文件把 `#/…` 导入改成从 `smart-admin-web` 导入，页面私有的子组件一并复制）——这一页从此归应用维护，不随内核升级。

推论：`api/index.ts` 里 `unwrap`/`ApiError`/`pageParams`/`toPage`/`unwrapDownload`/`normalizePreview`/`toWireRows` 的导出、`registerViews` / `registerLocales` / `registerMenuTitles`，以及三个扩展登记表——`registerMenuBadge`(菜单角标)、`registerHeaderTool`(顶栏工具)、`onRealtime`(在内置那条 SignalR 连接上挂自己的 hub 事件)——**是产品接缝而非普通导出**：包内没有调用方也必须留着，别当未使用代码清掉。做成登记表而不是插槽，是因为布局壳（`layouts/default.vue`、`AppHeader.vue`）在包里，应用改不到（用法见 `web/COMPONENTS.md`）。改动它们前先想清楚，别让应用只能靠整页复制内核文件才能扩展。

### 2.2 API 契约流

- **`schema.d.ts` 由后端 OpenAPI 生成**（`npm run gen:api`，后端需运行），**禁止手改**，改了重新生成。它分两层：内核那份在 `packages/admin/src/api/schema.d.ts`，导出为 `KernelPaths` / `KernelComponents`；应用那份在应用的 `src/api/schema.d.ts`，由模板的 `gen:api` 对着应用自己的后端生成，内核端点与应用端点都在里面。
- `api/client.ts` 是 `openapi-fetch` 针对 schema 的类型化封装。应用用 `createApiClient<paths>()` 建自己的客户端，中间件链（超时、Bearer + CSRF、401 刷新重放、40024 再认证）与内核同一条。客户端跟随运行期的 `apiBase`（由 `createSmartAdmin({ apiBase })` 设定），包里不读 `import.meta.env`：库构建会把它就地替换成包自己构建时的值。
- 内核 `api/index.ts` 按域分组导出（`authApi`/`personalApi`/`userApi`/`moduleApi`/`menuApi`…），每个方法 `client.X(...).then(r => unwrap<T>(r))`。应用的业务 API 新建 `src/api/<域>.ts`，从 `smart-admin-web` 导入 `unwrap`/`ApiError`/`pageParams`/`toPage` 即可（见 §2.1）。
- **`unwrap`** 统一解信封：2xx 的 `Result<T>`（code≠0 抛 `ApiError`）、非 2xx 的信封/ProblemDetails 都归一到 `ApiError`（带 `code`/`msgKey`）。视图 `catch` 后 `translateError(e)` 展示。参照 `api/index.ts`。
- 分页返回在 api 层归一为 `{ items, total }` 以适配 `SmartTable` 的 `:fetcher`（后端是 `PagedList<T>{current,size,total,items}`）。查询参数名用 PascalCase（ASP.NET 绑定要求）。

### 2.3 路由（静态 + 动态菜单注入）

- `router/routes.ts` 只放静态路由（login、error、shell/layout）。真实菜单树登录后从后端拉取，注入为**动态路由**（只活在内存）。
- **组件解析**（页面表 `router/viewRegistry.ts`）：内置页在包内登记（`import.meta.glob('../views/**/*.vue')`），插件与应用的页面经 `createSmartAdmin({ views })` 并入；key 取 glob 路径里 `/views/` 之后、去掉 `.vue` 的部分（如 `system/user/index`），就是菜单节点的 `component` 串，同 key 后注册者覆盖。`composables/useAuthMenu.ts` 按 key 取页面建路由：路由 `path` 取菜单 `path`，`name = menu-${id}`，挂在 `layout` 下；key 不在页面表里时注册 `MissingRoute` 诊断页，不静默 404。
- **详情页约定**：`views/<模块>/detail.vue`（key 以 `/detail` 结尾）自动成为 `/<模块>/:id/detail` 详情路由（`router/detailRoutes.ts`），随菜单路由一起重建。
- 应用在布局壳之外的顶级静态路由（整屏看板、打印页）经 `createSmartAdmin({ routes })` 并入。
- `namedPage`（`router/namedPage.ts`）包一层使“组件名===路由名”，供 keep-alive 的 `:include` 匹配。
- **F5/深链**：动态路由丢失，守卫（`router/index.ts`）在 `routesReady=false` 时调 `useModule().enterInitial()` 重建后重解析当前 URL。**不要持久化 `routesReady`/`menuTree`**（会跳过重建导致 404）。
- 登出/切应用用 `registerDynamic`/`resetRouter` 精确增删动态路由。

### 2.4 状态（Pinia）

- `defineStore` + `actions`；**按需持久化** `persist: { pick: [...] }`（`auth` 只存 `currentModuleId`，见 `stores/auth.ts` 顶部长注释解释为何其余不持久化）。
- 登出走 `reset()` 清授权态并清标签。
- 现有 store：`auth`(模块/菜单/权限码/permissionsLoaded/routesReady/isSuperAdmin)、`user`(令牌/登录态)、`app`(主题/偏好)、`tabs`(标签)、`dict`(字典选项缓存)。

### 2.5 组合式函数

- `use*` 命名，返回响应式引用与方法；**与 Naive 无关**，错误/消息回调由视图注入。确需 Naive Provider 的交互样板是明确例外（`useConfirm` 内部直接用 `useDialog`/`useMessage`），这类只能在 setup 里调用。
- 列表页统一用独立包 `smart-naive-table` 的 `SmartTable`：`:fetcher` 直接传 `xxxApi.page`，api 层负责把后端 `PagedList{current,size,total,items}` 归一成 `{items,total}`、把 `{page,pageSize}` 映射成 `{Current,Size}`。本仓**没有** `composables/useTable.ts`。用法约定见 `web/COMPONENTS.md` 的 SmartTable 一节。

### 2.6 按钮级权限

- `v-auth`（`directives/auth.ts`）：`v-auth="'POST:/api/v1/sys/user'"`（单码）/ 数组（默认 OR）/ `.and`（AND）；不命中置 `display:none`（不移除 DOM，权限刷新后 `watchEffect` 自动显隐）。
- 权限码来自 `GET /api/v1/personal/permissions`（登录后拉进 auth store）；仅取码**成功**时置 `permissionsLoaded`。显隐判定见 `authStore.hasPerm`：**超管**（`isSuperAdmin`）fail-open 全部显示；未加载（`permissionsLoaded=false`）fail-closed 隐藏；已加载的普通用户按码精确匹配，空集必然不命中即隐藏。服务端 `[RolePermission]` 始终兜底 403。

### 2.7 i18n

- 错误文案不出后端：后端给 `code` + `msgKey`（`ErrorCode.GetMsgKey()`，标注了 `[MsgKey]` 就是语义键如 `error.dict.typeNotFound`，没标注则兜底 `error.code.{数值}`），前端 `translateError` 出文案。
- **`translateError` 优先按 `ApiError.msgKey` 查 i18n**——键必须和 `[MsgKey]` 字符串逐字对上（嵌套形态）。没有 `msgKey` 时，按数字 `code` 查内置的 `CODE_MSG_KEY` 白名单兜底（`utils/error.ts`，只登记内核自己关心的少量码）；有 `msgKey` 但词典里没有这个键，就退回后端给的 `message`。消费者自定义错误码要标 `[MsgKey]` 并在 i18n 放同名键——按数字写 `error: { 60001: '...' }` 不在这份白名单里，读不到。
- 内置文案在包的 `locales/zh-CN.ts`/`en-US.ts`；应用的（含自定义错误码）放应用的 `src/locales/ext/<locale>/<模块>.ts`，经 `createSmartAdmin({ locales })` 深合并进内置，见 §2.1。
- 视图内所有可见文本走 `t('...')`，禁硬编码。

### 2.8 主题

- 令牌在 `styles/tokens.css` + `theme/`；Naive 主题 `theme/naive-theme.ts`。
- 首访跟随系统深浅（VueUse `usePreferredDark`），手动切换后由持久化接管。

### 2.9 组件 / 视图

- `<script setup lang="ts">`；表格列用 `h()` 渲染函数（`views/system/menu/index.vue` 是完整 CRUD 范例：`SmartTable` + `FormContainer` 弹窗表单 + `useConfirm` 二次确认）。
- 样式 `scoped`，用 CSS 变量（`var(--gap-card)` 等），不写死颜色/间距。

### 2.10 提交前检查

在 `web/` 下跑：

```bash
npm run lint         # oxlint（lint:fix 自动修）
npm run format:check # prettier，只查不改（format 自动修）
npm run typecheck    # vue-tsc --noEmit，包 + 模板
npm test             # vitest run（包）
npm run build        # 先构建包（类型检查 + 库构建），再构建模板应用
```

### 2.11 注释规范（`.ts` 约 15%、`.vue` 的 `<script>` 部分约 8%）

- 导出的 store/composable/指令加块注释说明用途与边界（现有 `auth.ts`/`useModule.ts`/`namedPage.ts` 是好范例）。
- `.vue` `<script setup>` 里复杂逻辑（树运算、成环校验、分页归一）加行内注释讲 WHY，只写现状。

---

## 附：目录速查

| 你要改… | 后端 | 前端 |
|---|---|---|
| 加实体/表 | `Services/Entities/` | — |
| 加接口/业务 | `Services/<域>/` + `ServicesSetup.cs` 注册 | 内核：`api/index.ts`；应用：`src/api/<域>.ts` |
| 加端点 | `AspNetCore/Controllers/` | — |
| 加页面 | — | 内核：`views/<模块>/<实体>/index.vue`；应用：`src/views/` 经 `createSmartAdmin({ views })`；再在菜单管理挂载 |
| 加缓存 | `Core/CacheKeys.cs` + 服务内 cache-aside | — |
| 加错误码 | `Core/ErrorCode.cs` | 内核：`locales/*`；应用：`src/locales/ext/<locale>/` |
| 加依赖版本 | `Directory.Packages.props` | `packages/admin/package.json`（持有全局单例的放 peerDependencies） |
