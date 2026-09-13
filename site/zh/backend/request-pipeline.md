# 请求管线

你给自己的控制器加一个新接口，只需在方法上放一个不带参数的 `[RolePermission]`，权限码是它自己从路由算出来的，比如 `GET:/api/v1/ping`。所以全站没有一个硬编码的权限字符串，加权限是在角色菜单界面上勾一下路由。管线其余三关也是这个路子：位置固定，调用方零参数。

## 全景

```text
HTTP 请求
  │
  ├─①  认证   Microsoft JWT Bearer
  │          claim 不映射(sub / sid / sadm / unique_name)
  │          框架 401 challenge → 统一信封(40006)
  │
  ├─②  [RolePermission]   授权过滤器
  │          未认证 → 401;超管 sadm → 放行
  │          校验会话 sid 是否仍活跃(强退即时生效)
  │          权限码 = {METHOD}:/{路由模板},比对用户权限码集合
  │
  ├─③  数据范围   解析生效机构集,写入 IDataScopeContext
  │
  └─④  结果信封   裸 return dto → Result<T>
             AdminException / ErrorCode → 信封(数字码,不下发文案)
```

## ① 认证：Microsoft JWT Bearer

内核直接用 `Microsoft.AspNetCore.Authentication.JwtBearer`，不自己造一套认证栈。装配在 `SmartAdminSetup.cs`：

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SymmetricSecurityKey>((o, signingKey) =>
    {
        o.MapInboundClaims = false;   // 保留原始 claim 名,不做遗留映射
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Jwt.Issuer,
            IssuerSigningKey = signingKey,
            ValidateAudience = false,          // 单体后台,不启用 audience
            ValidateLifetime = true,           // 校验 exp / nbf
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
        };
        o.Events = new JwtBearerEvents { OnChallenge = /* 见下 */ };
    });
```

**Claim 不映射**。.NET 默认有一套遗留映射，会把 `sub` 改写成一长串 XML namespace URI。`MapInboundClaims = false` 把它关掉，令牌里的 claim 名就原样保留了。内核约定的那几个自定义 claim 名，集中在 `TokenClaimNames`，代码在 `Core/Security/ITokenProvider.cs`。

| Claim | 常量 | 含义 |
| --- | --- | --- |
| `sub` | `JwtRegisteredClaimNames.Sub` | 用户主键 |
| `sid` | `TokenClaimNames.SESSION_ID` | 会话标识（强退锚点） |
| `sadm` | `TokenClaimNames.SUPER_ADMIN` | 超管标志（值为 `"true"` 时授权直接放行） |
| `org` | `TokenClaimNames.ORG_ID` | 归属机构 Id（数据范围锚点） |
| `unique_name` | `JwtRegisteredClaimNames.UniqueName` | 登录账号，映射为 `User.Identity.Name` |

**框架 401 被重塑成统一信封**。令牌缺失或过期的时候，JwtBearer 默认返回一个空的 401，响应体不是内核的信封格式。`OnChallenge` 把它接管过来，改写成和业务出口一致的 `Result<T>`：

```csharp
OnChallenge = async ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
};
```

`ErrorCode.TokenInvalid` 对应的数字码是 40006。这么一来，前端不管碰到「令牌过期」还是「无权限」，拿到的都是同构的信封，可以按码统一处理。

::: tip 默认拒绝
控制器端点经 `MapControllers().RequireAuthorization()` 默认都要求认证，但尊重 `[AllowAnonymous]`。登录、验证码这些匿名端点显式放行，其余的一律先过认证这一关。
:::

机器端另有第二个 scheme `ApiKey`：端点挂 `[ApiKey]` 就只认请求头里的预共享密钥，不认用户 JWT；无 key 或错 key 回 401 + `40027`。key 绑定了用户的话，后面的 `[RolePermission]`、数据范围、操作日志都按那个用户走，不另造一套。写法与配置见「认证与安全」的「API Key 接入」一节。

## ② `[RolePermission]`：权限码就是路由

授权由 `RolePermissionAttribute` 承担，它实现的是 `IAsyncAuthorizationFilter`。这个特性**不带参数，也不带权限字符串**。代码里永远不会出现 `"sys:user:add"` 这种魔法串。授权是靠在角色-菜单界面上勾选路由完成的。

过滤器内部按固定顺序执行：

```csharp
// 1. 必须已通过 JWT 认证
if (user.Identity?.IsAuthenticated != true)
    → 401 + 40006

// 2. 会话活性校验:sid 对应会话被吊销/过期 → 401(超管同样受此约束)
var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
if (!await sessions.IsActiveAsync(sessionId))
    → 401 + 40006

// 3. 超管直接放行 + 数据范围不受限
if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
{
    scopeContext.Current = DataScopeResult.Unrestricted;
    return;
}

// 4. 普通用户:解析数据范围写入上下文(见 ③)
// 5. 权限码比对
var code = PermissionCode.Build(method, routeTemplate);
if (!codes.Contains(code)) → 403 + 41001
```

**权限码 = 规范化路由**。`PermissionCode.Build` 是唯一真源：

```csharp
public static string Build(string httpMethod, string? routeTemplate) =>
    $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";
// 例:GET:/api/v1/ping
```

这里用的是路由模板，不是实际路径。所以带参数的路由，权限码是稳定的，不随参数值变化，比如 `user/{id}`。同一个 `Build` 函数有三处共用：授权比对、`MenuController.Routes` 路由清单（喂给菜单表单的权限码下拉）、操作日志的缺省操作名。为什么要共用一个函数？这样「授权时算的码」和「菜单里存的码」才不会因为大小写、斜杠差了一个字符就静默对不上。

### 一颗按钮，多条路由

授权的单元是菜单按钮，不是路由。一颗按钮的 `Permission` 字段可以挂多条码，以 `;` 连接；角色勾上这颗按钮，这些路由一起授出。内核种子里每个页面固定四颗：查询、新增、更新、删除。「查询」覆盖列表与详情两个接口，「删除」把单条删除和批量删除收在一起，导入的四步也只是一颗「导入」。页面独有的操作（重置密码、启停、执行一次）才各占一颗。

拆合规则只有一处，`PermissionCode.Split` 与 `PermissionCode.Join`：按 `;` 拆开、去空白、逐条重新规范化、去重。`RbacPermissionProvider` 聚合用户权限码时先拆再并集，`MenuService` 保存按钮前先拆再拼回，所以 `get:/API/v1/Ping` 到不了库里。授权判定始终按单条路由进行：`[RolePermission]` 拿请求路由算出的码去比对用户集合，一个勾选框只是展开成几条路由。前端 `v-auth` 也照旧写单条路由码。

**会话活性校验让强制下线立即生效**。第 2 步每个请求都会调一次 `ISessionService.IsActiveAsync(sid)`。管理员在「在线用户」里踢完人，这个会话的缓存就被移除、库里也标记成吊销。被踢用户手里的 access token 哪怕还没到期，下一个请求照样 401。超管也不例外。

::: tip `[ActiveSession]`：任意登录用户端点
个人中心、登出这类端点，任何已登录用户都能用，但不需要具体的权限码。给它们挂 `ActiveSessionAttribute` 就行。它只做上面的第 1、2 步，也就是认证加会话活性，跳过权限码比对。要是只挂 `[Authorize]` 不挂它，会话被强退之后，没过期的令牌还能接着调。所以想让强退即时生效的端点，必须挂 `[ActiveSession]`。
:::

## ③ 数据范围：解析并注入 `IDataScopeContext`

授权阶段的第 3、4 步，顺带把当前用户的**生效数据范围**解析出来，写进 `IDataScopeContext`：

```csharp
// 超管
scopeContext.Current = DataScopeResult.Unrestricted;

// 普通用户(走缓存)
var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
scopeContext.Current = await dataScopeProvider.ResolveAsync(userId, abort);
```

为什么要在授权阶段、也就是动作执行之前就写入？因为动作里的 DB 查询要用到它。写进去之后，这个请求后续对 `DataEntity` 的查询，都会被 SqlSugar 全局过滤器自动按机构集过滤，业务代码一行过滤条件都不用写。数据范围的完整机制见 [多组织数据权限](./data-scope.md)。

## ④ 结果信封：裸返回也被包好

到了返回阶段，内核会统一把出参包成信封 `Result<T>`，把业务错误也转成信封。

**成功：裸 `return dto` 自动包壳**。有了 `ResultEnvelopeFilter`，业务控制器直接 `return dto;` 也能拿到统一信封，不用每个地方都手写 `Result.Ok(...)`。这个过滤器实现的是 `IAsyncResultFilter`：

```csharp
public static bool TryWrap(IActionResult result, out ObjectResult wrapped)
{
    wrapped = null!;
    if (result is not ObjectResult obj) return false;        // File/StatusCode 等不动
    if (obj.Value is IResultEnvelope) return false;          // 已是信封,放行
    if (obj.StatusCode is int sc && (sc < 200 || sc >= 300)) return false; // 非 2xx 不包
    wrapped = new ObjectResult(Result<object?>.Ok(obj.Value)) { StatusCode = obj.StatusCode };
    return true;
}
```

它只包**成功（2xx）的裸 `ObjectResult`**。File、StatusCode、错误结果一律不动。内置控制器仍然显式返回 `Result<T>`，为的是保住 OpenAPI 契约，所以对它们来说这个过滤器是空操作。

::: tip 契约也跟着包了
过滤器是在结果执行阶段才包信封的，ApiExplorer 看不到这一步。契约生成按同一条规则（声明类型不是信封就会被包）把 200 的 schema 补上信封外壳：消费方端点裸返回 `dto`，契约里记的也是包了信封的形状，重新 `gen:api` 拿到的前端类型才是对的——不然生成的类型会把 `data` 当成顶层字段，不报错、直接拿不到值。内置控制器全部显式返回 `Result<T>`，不受影响。
:::

**失败：`AdminException` → 信封**。账密错、验证码错、无权限这类可预期的业务失败，都抛 `AdminException`。`AdminExceptionFilter` 把它转成 HTTP 200 加业务码信封：

```csharp
public void OnException(ExceptionContext context)
{
    if (context.Exception is not AdminException ex) return;
    if (ex.InnerException is null)
        logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    else
        logger.LogWarning(ex.InnerException, "业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    context.Result = new ObjectResult(Result<object>.From(ex));
    context.ExceptionHandled = true;
}
```

业务失败记的是 **Information** 级日志，因为它不是错误，不该去打扰告警。带 `InnerException` 的那部分记 **Warning**，比普通业务失败多响一格，因为这通常是包了一层的外部调用失败，比如磁盘满、上游鉴权过期。其他异常不在这里拦。框架默认的 500 流程会接手它们，保留完整堆栈。程序缺陷就该大声失败。

**错误是数字码，从不下发本地化文案**。信封里带的是 `{ code, msgKey, args, message }`，其中 `code` 是 `ErrorCode` 枚举的数字值。i18n 交给前端按码翻译，后端不返回中文或英文的错误文案。

## 分页超上限是报错，不是截断

每页条数超过 `SmartAdmin:Api:MaxPageSize`（默认 200）时抛 `48001 PageSizeExceeded`。

```json
{
  "SmartAdmin": {
    "Api": { "MaxPageSize": 200 }
  }
}
```

不设这道拦截的话，超限请求会被悄悄按上限查、再返回成功，调用方拿到的是一个「少了大半却看不出来」的结果。拿分页当导出用的地方尤其吃这一刀：以为导全了，实际只有前 200 行。要更大的页就调 `MaxPageSize`；要全量请用显式的导出写法，别借道分页。

## 契约里的四个扩展字段

`/openapi/v1.json` 的每个操作都带四样东西，都是机器要读的：

| 字段 | 值 |
| --- | --- |
| `operationId` | `{控制器}_{动作}`，重名时按顺序加后缀。没有它，光看动作名，`Delete` 会撞出十个、`Update` 会撞出八个，全靠路径区分，SDK 生成器和 LLM 工具调用只能靠猜 |
| `x-permission-code` | 该端点的权限码，如 `GET:/api/v1/sys/user/page`。只有挂了 `[RolePermission]` 的才有 |
| `x-auth` | `anonymous` \| `apikey` \| `permission` \| `session` \| `authenticated` |
| `x-module` | 所属模块名，也就是能经 `Api:DisabledModules` 整体关掉的那个名字 |

`x-permission-code` 与授权判定同源，两边都走 `PermissionCode.Build`，所以契约和角色授权页不会各说各话。

错误码目录也在这份契约里，见[错误码](./error-codes.md)。

## 一次调用回顾

以「删除某角色」为例：

1. **认证**：校验 JWT 签名与有效期，读出 `sub` / `sid` / `sadm`。
2. **`[RolePermission]`**：会话 `sid` 仍然活跃，用户不是超管，权限码 `DELETE:/api/v1/sys/role/{id}` 也在用户的权限码集合里，于是放行。同时把该用户的数据范围写入 `IDataScopeContext`。
3. **数据范围**：仓储的写路径守卫先用带范围过滤器的查询，确认目标行在范围内。越权改删别的机构的行，返回 0 行被拒。
4. **结果信封**：控制器 `return` 的结果被包成 `Result<T>`。中途要是抛了 `AdminException`，就转成业务码信封返回。
