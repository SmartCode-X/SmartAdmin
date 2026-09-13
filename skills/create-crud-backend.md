# 创建后端 CRUD (Create Backend CRUD)

为一个已有实体创建完整的后端 CRUD 服务栈。如果实体还没建，先参考 `create-entity.md`。

产出共 6 处文件/改动（以 `Position` 为例说明规则）。

## 第一步：确定模式

- **系统模块**：文件放 `backend/src/SmartAdmin.Services/{Module}/`，Controller 放 `backend/src/SmartAdmin.AspNetCore/Controllers/`，路由 `api/v1/sys/{module}`
- **业务模块**：文件放消费者 Assembly 中，路由 `api/v1/biz/{module}` 或自定前缀

---

## 产出 1：Models（DTO）

文件：`{Module}Models.cs`，放 Service 同目录。

### 规则

- 用 C# `record` 类型，属性 `{ get; init; }`
- 新增/编辑共用同一个 `{Entity}Input` record
- 分页查询用 `{Entity}PageInput : PageInputBase`，只加过滤字段
- `PageInputBase` 已含 `Current`, `Size`, `SortField`, `SortOrder`（在 `SmartAdmin.Core` 命名空间）
- 命名空间与 Service 一致

### 参考模板

```csharp
// 文件: backend/src/SmartAdmin.Services/Position/PositionModels.cs
using SmartAdmin.Core;

namespace SmartAdmin.Services;

public record PositionInput
{
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public int Sort { get; init; }
    public bool Enabled { get; init; } = true;
}

public record PositionPageInput : PageInputBase
{
    public string? Name { get; init; }
}
```

---

## 产出 2：Interface（服务接口）

文件：`I{Module}Service.cs`，放 Service 同目录。

### 规则

- 标准五方法签名：`PageAsync`, `GetAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`
- 分页返回 `PagedList<TEntity>`（`SmartAdmin.Core` 命名空间）
- Add 返回 `long`（新 Id）
- Update/Delete 返回 `Task`（无返回值）

### 参考模板

```csharp
// 文件: backend/src/SmartAdmin.Services/Position/IPositionService.cs
using SmartAdmin.Core;

namespace SmartAdmin.Services;

public interface IPositionService
{
    Task<PagedList<SysPosition>> PageAsync(PositionPageInput input);
    Task<SysPosition> GetAsync(long id);
    Task<long> AddAsync(PositionInput input);
    Task UpdateAsync(long id, PositionInput input);
    Task DeleteAsync(long id);
}
```

---

## 产出 3：Service（服务实现）

文件：`{Module}Service.cs`，放 Service 同目录。

### 规则

- **主构造函数注入** `IRepository<TEntity>`（`SmartAdmin.SqlSugar` 命名空间）
- **所有方法 `virtual`**——这是可替换性核心，消费者靠覆写单步来定制
- 错误守卫用 `AdminException.ThrowIf(condition, ErrorCode.Xxx)`
- 唯一性检查（Code 等唯一字段）必须用 `.ClearFilter<ISoftDelete>()` 包含软删行，防止 DB 唯一索引冲突
- 分页用 `.WhereIF(condition, predicate)` 链式过滤 + `.ToPagedListAsync(input, defaultOrder)`
- Get 后判 null → 抛 `ErrorCode.XxxNotFound`
- Delete 是软删（`IRepository.DeleteAsync` 自动软删）
- **插入后要新行 Id：直接读实体**。`InsertAsync(entity)` 之后 `entity.Id` 就是新 Id（雪花 Id 由 AOP 在插入前写进实体，模板里 `return position.Id` 就是这么来的）。**禁用 `ExecuteReturnBigIdentityAsync` / `ExecuteReturnIdentityAsync`**，原因见「容易忽略的点 → 6」

### 参考模板

```csharp
// 文件: backend/src/SmartAdmin.Services/Position/PositionService.cs
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

public class PositionService(IRepository<SysPosition> positions) : IPositionService
{
    public virtual async Task<PagedList<SysPosition>> PageAsync(PositionPageInput input) =>
        await positions.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), p => p.Name.Contains(input.Name!))
            .ToPagedListAsync(input, q => q.OrderBy(p => p.Sort));

    public virtual async Task<SysPosition> GetAsync(long id)
    {
        var position = await positions.GetByIdAsync(id);
        AdminException.ThrowIf(position is null, ErrorCode.PositionNotFound);
        return position!;
    }

    public virtual async Task<long> AddAsync(PositionInput input)
    {
        // 唯一性检查：ClearFilter<ISoftDelete>() 纳入软删行
        AdminException.ThrowIf(
            await positions.AsQueryable().ClearFilter<ISoftDelete>()
                .AnyAsync(p => p.Code == input.Code),
            ErrorCode.PositionCodeExists);

        var position = new SysPosition
        {
            Name = input.Name,
            Code = input.Code,
            Sort = input.Sort,
            Enabled = input.Enabled,
        };
        await positions.InsertAsync(position);
        return position.Id;
    }

    public virtual async Task UpdateAsync(long id, PositionInput input)
    {
        var position = await GetAsync(id);
        AdminException.ThrowIf(
            input.Code != position.Code &&
            await positions.AsQueryable().ClearFilter<ISoftDelete>()
                .AnyAsync(p => p.Code == input.Code && p.Id != id),
            ErrorCode.PositionCodeExists);

        position.Name = input.Name;
        position.Code = input.Code;
        position.Sort = input.Sort;
        position.Enabled = input.Enabled;
        await positions.UpdateAsync(position);
    }

    public virtual async Task DeleteAsync(long id)
    {
        await GetAsync(id);
        await positions.DeleteAsync(id);
    }
}
```

---

## 产出 4：ErrorCode

文件：`backend/src/SmartAdmin.Core/ErrorCode.cs`（系统模块）或消费者自定义枚举（业务模块）。

### 规则

- 按分段规划放置（见 ErrorCode.cs 注释头部的分段表）
- 每个码必须标 `[MsgKey("error.{module}.{name}")]`
- 典型码：`{Entity}NotFound`, `{Entity}CodeExists`（如有唯一字段）
- 对应的前端 i18n key 也要同步添加

### 系统模块示例（在已有的 42xxx 段追加）

```csharp
/// <summary>目标职位不存在</summary>
[MsgKey("error.position.notFound")]
PositionNotFound = 42005,

/// <summary>职位编码已存在(编码唯一)</summary>
[MsgKey("error.position.codeExists")]
PositionCodeExists = 42010,
```

### 业务模块示例（消费者自选码段）

`AdminException` 的构造函数只收内核的 `ErrorCode` 枚举类型——消费者**不能**定义一个新枚举类型传进去，而是把自选数字（从 60000 起步，避开内核 4xxxx/5xxxx 段）**强转**后使用：

```csharp
throw new AdminException((ErrorCode)60001);
```

未标注 `[MsgKey]` 的码，msgKey 自动回退为 `error.code.60001`——前端语言包加这个键即可翻译。建议把数字集中到一个常量类（如 `public static class BizErrorCode { public const int ProductNotFound = 60001; }`）防止散落漂移。

---

## 产出 5：DI 注册

### 系统模块

在 `backend/src/SmartAdmin.Services/ServicesSetup.cs` 的 `AddSmartAdminServices` 方法中追加：

```csharp
services.TryAddScoped<IPositionService, PositionService>();
```

**必须用 `TryAddScoped`**（不是 `AddScoped`），这是可替换性契约——消费者可在 `AddSmartAdmin()` 之前注册自己的实现来覆盖。

### 业务模块

消费者在自己的 `Program.cs` 或 Setup 扩展中注册：

```csharp
builder.Services.AddScoped<IProductService, ProductService>();
```

业务模块用 `AddScoped` 即可（不需要 TryAdd，因为不存在被覆盖的场景）。

---

## 产出 6：Controller

文件：`{Module}Controller.cs`

### 规则

- 继承 `ControllerBase`（ASP.NET Core 的，不是 `Controller`）
- 标注 `[ApiController]` + `[Route("api/v1/sys/{module}")]`（业务模块用 `api/v1/biz/`）
- 主构造函数注入 `I{Module}Service`
- 命名空间：系统模块 `SmartAdmin.AspNetCore`，业务模块自定
- 每个 action 标 `[RolePermission]`（权限码 = 路由，后台菜单管理中按路由配权限）
- 写操作可加 `[OperationLog("描述")]` 记录操作日志
- 返回值统一用 `Result<T>.Ok(...)` 包装

### HTTP 动词规范

| 操作 | 动词 | 路由 | 返回 | 权限按钮 |
|---|---|---|---|---|
| 列表 | `GET` | `page` | `Result<PagedList<T>>` | 查询 |
| 详情（按 Id） | `GET` | `{id}` | `Result<T>` | 查询 |
| 新增 | `POST` | `add` | `Result<long>` | 新增 |
| 更新 | `PUT` | `{id}` | `Result<bool>` | 更新 |
| 删除 | `DELETE` | `{id}` | `Result<bool>` | 删除 |
| 批量删除 | `POST` | `batch-delete` | `Result<bool>`（入参 `BatchDeleteInput`） | 删除 |
| 全量列表 | `GET` | `list` | `Result<IReadOnlyList<T>>`（树/下拉等不分页场景，替代 `page`，参照 `OrgController`） | 查询 |

最后一列是这条路由归哪颗权限按钮：一个页面固定四颗（查询 / 新增 / 更新 / 删除），一颗按钮可挂多条路由（`;` 连接），角色勾上即一并授出。列表与详情同属「查询」，批量删除并入「删除」；导入、导出、启停这类页面独有的操作才各加一颗。

### 参考模板

```csharp
// 文件: backend/src/SmartAdmin.AspNetCore/Controllers/PositionController.cs
using Microsoft.AspNetCore.Mvc;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.AspNetCore;

[ApiController]
[Route("api/v1/sys/position")]
public class PositionController(IPositionService positionService) : ControllerBase
{
    [HttpGet("page")]
    [RolePermission]
    public async Task<Result<PagedList<SysPosition>>> Page([FromQuery] PositionPageInput input) =>
        Result<PagedList<SysPosition>>.Ok(await positionService.PageAsync(input));

    [HttpGet("{id}")]
    [RolePermission]
    public async Task<Result<SysPosition>> Get(long id) =>
        Result<SysPosition>.Ok(await positionService.GetAsync(id));

    [HttpPost("add")]
    [RolePermission]
    [OperationLog("新增职位")]
    public async Task<Result<long>> Add(PositionInput input) =>
        Result<long>.Ok(await positionService.AddAsync(input));

    [HttpPut("{id}")]
    [RolePermission]
    [OperationLog("更新职位")]
    public async Task<Result<bool>> Update(long id, PositionInput input)
    {
        await positionService.UpdateAsync(id, input);
        return Result<bool>.Ok(true);
    }

    [HttpDelete("{id}")]
    [RolePermission]
    [OperationLog("删除职位")]
    public async Task<Result<bool>> Delete(long id)
    {
        await positionService.DeleteAsync(id);
        return Result<bool>.Ok(true);
    }
}
```

---

## 容易忽略的点

### 1. 菜单种子数据（系统模块必做）

**这是最容易忘的步骤。** 不加菜单种子，页面不会出现在导航中，权限按钮也不存在。

在 `backend/src/SmartAdmin.Services/Seed/DefaultMenuSeed.cs` 中追加。结构：一条 `MenuType.Menu`（页面节点）+ 固定四颗 `MenuType.Button`（查询 / 新增 / 更新 / 删除）+ 该页独有操作各一颗。

**取号规则**（见 `DefaultMenuSeed` 头部注释，`MenuSeedIdLayoutTests` 锁定）：

- **百位是分区**：1xx 工作台、2xx 组织管理、3xx 系统运维、4xx 任务调度、5xx 日志审计、6xx 文件管理，7xx–9xx 留给新目录。整百是目录本身。
- **十位是页面**：页面 Id 取整十，一页独占一个十位段，新页面接在所属目录已有页面之后。一个目录最多九页（X10–X90）。
- **个位是按钮，语义固定**：**1 查询、2 新增、3 更新、4 删除**，5–9 放本页特有操作（导入、导出、启停……）。页面没有的标准按钮空着位，不往前挪。测试会核对 1–4 号位的 HTTP 方法：1 全是 GET，2 全是 POST，3 全是 PUT，4 含 DELETE。
- **目录下无页面的接口按钮**取目录的 01–09（如 301 探针）。
- 内核种子上限 999；撞号、越界会被启动检查与 `SeedIdRangeTests` 当场拒绝。

```csharp
// 页面节点:Component 对应前端 views/ 下的路径
new SysMenu {
    Id = 220, ParentId = 200,                    // 组织管理(200)下第二个页面
    Type = MenuType.Menu, Title = "岗位管理", Permission = "",
    Path = "/system/position", Component = "system/position/index",
    Icon = "ph:identification-badge-duotone", Sort = 2,
    Enabled = true, Visible = true,
},
// 权限按钮:Permission = "METHOD:/路由模板",权限码就是路由;一颗按钮挂多条路由用 PermissionCode.Join
new SysMenu { Id = 221, ParentId = 220, Type = MenuType.Button,
    Title = "岗位-查询",   Permission = PermissionCode.Join(["GET:/api/v1/sys/position/page", "GET:/api/v1/sys/position/{id}"]), Sort = 1, Enabled = true },
new SysMenu { Id = 222, ParentId = 220, Type = MenuType.Button,
    Title = "岗位-新增",   Permission = "POST:/api/v1/sys/position/add",      Sort = 2, Enabled = true },
new SysMenu { Id = 223, ParentId = 220, Type = MenuType.Button,
    Title = "岗位-更新",   Permission = "PUT:/api/v1/sys/position/{id}",      Sort = 3, Enabled = true },
new SysMenu { Id = 224, ParentId = 220, Type = MenuType.Button,
    Title = "岗位-删除",   Permission = PermissionCode.Join(["DELETE:/api/v1/sys/position/{id}", "POST:/api/v1/sys/position/batch-delete"]), Sort = 4, Enabled = true },
```

**Permission 格式 = `METHOD:/路由模板`**，与 Controller 路由严格一致；多条以 `;` 连接（`PermissionCode.Join` / `Split`）。前端按钮门控的值仍是单条路由码（`v-auth` 指令），与按钮挂几条无关。`PermissionCodeConsistencyTests` 双向锁：种子里每条码都要有真实端点，每个 `[RolePermission]` 端点都要出现在某颗按钮里——新加受权端点必须同批进种子。

**接口没有页面时**（只给移动端 / PDA / 第三方系统调）：不要建 `Visible=false`、`Path=""` 的假页面当容器。建一个 `MenuType.Catalog` 目录当权限组（不填 `Path`/`Component`），把 `MenuType.Button` 直接挂在它下面。后端权限收集不看节点类型与层级；前端不生成路由、侧栏不显示；角色授权界面把它们渲染成该目录组里「接口权限(无页面)」一行。内核的 `GET:/api/v1/ping`（`Id=301`，挂在「系统运维」目录 300 下）就是现成写法。

业务模块通常通过后台「菜单管理」UI 添加，而不是写种子数据。

### 2. 批量删除

如果需要批量删除，在 Service 接口加 `DeleteBatchAsync`，Controller 加 `batch-delete` 端点：

```csharp
// Service
public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
{
    foreach (var id in ids)
        await DeleteAsync(id);  // 复用单删的守卫逻辑
}

// Controller
[HttpPost("batch-delete")]
[RolePermission]
[OperationLog("批量删除职位")]
public async Task<Result<bool>> BatchDelete(BatchDeleteInput input)
{
    await positionService.DeleteBatchAsync(input.Ids);
    return Result<bool>.Ok(true);
}
```

`BatchDeleteInput` 已定义在 `SmartAdmin.Core`，直接复用。

### 3. 删除前检查引用

如果此实体被其他表引用（如职位被用户引用），删除前应检查：

```csharp
public virtual async Task DeleteAsync(long id)
{
    await GetAsync(id);
    // 检查是否仍被用户引用
    AdminException.ThrowIf(
        await users.AsQueryable().AnyAsync(u => u.PositionId == id),
        ErrorCode.PositionInUse);  // 需在 ErrorCode 中新增此码
    await positions.DeleteAsync(id);
}
```

### 4. `[OperationLog]` 审计日志

**所有写操作（增/改/删）都应加 `[OperationLog("描述")]`。** 读操作不加。系统内所有内置 Controller 的写端点都已标注此属性。操作日志过滤器会记录入参（密码等敏感字段自动脱敏）。

### 5. 缓存与事件总线

如果模块数据会被频繁读取或其他模块依赖（类似字典/配置），需注入 `ICacheProvider` + `IEventBus`：

- 读接口加缓存（`ICacheProvider.GetOrCreateAsync`）
- 写接口写完后失效缓存 + 发布事件（`IEventBus.PublishAsync`）
- 参考 `DictService` / `ConfigService` 的实现

普通 CRUD 模块不需要，仅当读频率远大于写频率时考虑。

### 6. 新行 Id 与批量插入（SqlSugar + 雪花 AOP 的两个坑）

内核实体主键全是雪花值，没有自增列。`Id` 由 `SqlSugarSetup` 里的 AOP 在插入前写到实体对象上，所以：

```csharp
await positions.InsertAsync(position);        // ✅ 之后 position.Id 就是新行 Id
await SyncChildrenAsync(position.Id, ...);     //    直接拿去挂子行
```

**不要**写 `x.Id = await db.Insertable(x).ExecuteReturnBigIdentityAsync();`：没有 identity 列时它返回 0，还会把 AOP 已经分配好的正确 Id **覆盖成 0**。父行本身能存进去（库里的 Id 是对的），但之后凡是拿 `x.Id` 去挂子行的，全部写成父键 0 的孤儿行，界面上静默消失、也不报错，很难排查。

同理，`db.Insertable(list).ExecuteCommandAsync()` 批量插入**不回填**各行 Id；需要拿子行 Id 再挂孙行时只能逐条插。除此之外集合写库一律整批——一次查存在性、一次事务、`Insertable(list)` 分片插。`foreach` 里逐条查库 / 插库 / 开事务量小看不出来，上了量就是接口超时的头号来源，而且循环里开事务等于放弃整批原子性，中途失败留下半批数据。

---

## 检查清单

完成所有产出后，运行以下验证：

```bash
dotnet build backend/SmartAdmin.slnx -c Release
dotnet test backend/SmartAdmin.slnx
```

后端启动后，用 `/openapi/v1.json` 确认新端点已暴露，然后重新生成前端契约类型：系统模块在 `web/` 下执行 `npm run gen:api`（写 `web/packages/admin/src/api/schema.d.ts`）；业务模块在消费方应用根目录执行 `npm run gen:api`（写 `src/api/schema.d.ts`）。
