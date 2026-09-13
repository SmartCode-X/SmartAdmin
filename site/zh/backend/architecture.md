# 架构分层与包依赖

`backend/src` 下一共十二个 NuGet 包（另有仓库根 `templates/` 下的脚手架包 `SmartAdmin.Templates`，发版共十三个）。其中五个构成核心链条，依赖方向只能自上而下：上层能引下层，下层永远看不见上层。哪一层把这个方向反转，可替换性和依赖红线就一起垮掉。六个只挂在 `Core` 旁边，是主链之外的可选支线；剩下一个是测试支撑包，不进这条主链，也不进元包。

## 核心链条：五个包

```text
SmartAdmin.Core        纯契约:接口(I*Provider、I*Service)、Options、Result<T>、ErrorCode、AdminException。
   ↑                   无 SqlSugar、无 ASP.NET。
SmartAdmin.SqlSugar    数据层:ISqlSugarClient 单例(SqlSugarScope)、IRepository<>、实体基类、
   ↑                   CodeFirst DatabaseInitializer、种子运行器。
SmartAdmin.Services    领域层:实体(Sys*)、*Service 实现、RBAC / 数据范围提供者、事件总线。
   ↑                   实体定义在这一层,不在 SqlSugar 层。
SmartAdmin.AspNetCore  宿主集成:AddSmartAdmin / MapSmartAdmin、JWT、[RolePermission] / [ActiveSession]
                       过滤器、内置控制器、信封 / 异常 / 操作日志过滤器。

SmartAdmin             元包:只引用 AspNetCore。消费方装这一个,即传递引入整条栈。
```

六个旁支都只依赖 `Core`，而 Core/SqlSugar/Services/AspNetCore 都不会反过来引用它们：

```text
SmartAdmin.Caching.Redis   可选包:RedisCacheProvider(基于 StackExchange.Redis 的 ICacheProvider 实现),
                            消费方在 AddSmartAdmin() *之前* 调用 AddSmartAdminRedisCache(configuration) 即可启用。
SmartAdmin.Auth.WeCom      可选包:企业微信登录(桌面扫码、客户端内网页授权)的 IExternalAuthProvider 实现。
SmartAdmin.Auth.DingTalk   可选包:钉钉扫码登录的 IExternalAuthProvider 实现。
SmartAdmin.Auth.GitHub     可选包:GitHub OAuth App 登录的 IExternalAuthProvider 实现。
SmartAdmin.Auth.WeChat     可选包:微信开放平台网站应用扫码登录的 IExternalAuthProvider 实现。
SmartAdmin.Excel           可选包:xlsx 读写与带下拉的模板生成,消费方在 AddSmartAdmin() *之前*
                            调用 AddSmartAdminExcel() 即可启用。
   ↑
SmartAdmin.Core
```

四个登录可选包连第三方运行时依赖都没有，只引 Microsoft.*。依赖红线对可选包同样成立。另有一个测试支撑包 `SmartAdmin.Testing`，它不进这张依赖图，也不引用任何内核包——定位见[项目结构](./structure.md)。

各层职责与依赖方向：

| 包 | 职责 | 依赖 | 第三方运行时依赖 |
| --- | --- | --- | --- |
| `SmartAdmin.Core` | 契约、Options、`Result<T>`、`ErrorCode`、`AdminException`、`IIdGenerator` | 无 | 仅 Microsoft.* |
| `SmartAdmin.SqlSugar` | `SqlSugarScope` 单例、`IRepository<>`、`BaseEntity`/`DataEntity`、CodeFirst、种子 | Core | SqlSugarCore |
| `SmartAdmin.Services` | `Sys*` 实体、服务实现、RBAC、数据范围、[事件总线](/zh/backend/event-bus) | SqlSugar、Core | SqlSugarCore |
| `SmartAdmin.AspNetCore` | JWT、授权过滤器、内置控制器、全局过滤器、`AddSmartAdmin` | Services、SqlSugar、Core | Microsoft.AspNetCore.* |
| `SmartAdmin`（元包） | 聚合入口 | AspNetCore |——|
| `SmartAdmin.Caching.Redis`（可选） | `RedisCacheProvider`：Redis 版 `ICacheProvider` | 仅 Core | StackExchange.Redis |
| `SmartAdmin.Auth.WeCom`（可选） | 企业微信登录（桌面扫码、客户端内网页授权）的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `SmartAdmin.Auth.DingTalk`（可选） | 钉钉扫码登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `SmartAdmin.Auth.GitHub`（可选） | GitHub OAuth App 登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `SmartAdmin.Auth.WeChat`（可选） | 微信开放平台网站应用扫码登录的 `IExternalAuthProvider` | 仅 Core | 仅 Microsoft.* |
| `SmartAdmin.Excel`（可选） | xlsx 读写与模板生成的 `IExcelReader`/`IExcelWriter`/`IExcelTemplateBuilder` | 仅 Core | MiniExcel、DocumentFormat.OpenXml |
| `SmartAdmin.Testing`（测试支撑，不进元包） | `AdminAppFactory<TEntryPoint>`、四方言 `TestDb`、HTTP 信封小助手 | 不引用任何内核包（只认配置键与 HTTP） | `Microsoft.AspNetCore.Mvc.Testing`、`Microsoft.Data.Sqlite`/`SqlClient`，加 `MySqlConnector`、`Npgsql` 两个方言驱动 |

`SmartAdmin.Caching.Redis` 没有引入新机制，就是把内核那套 `TryAdd` 可替换性套用在缓存提供者上。消费方在 `AddSmartAdmin()` 之前调用 `AddSmartAdminRedisCache(configuration)`。它内部用 `TryAddSingleton` 注册 `RedisCacheProvider`，抢先赢下注册，替换掉内核默认的进程内 `MemoryCacheProvider`。不调用这个方法，或者没把 `SmartAdmin:Cache:Provider` 配成 `Redis`，内核的进程内默认实现照常工作，不受影响。

`SmartAdmin.Excel` 走的是同一条路：内核默认注册的三个 codec 全是 `MissingExcelProvider`，一调就抛 `ErrorCode.ExcelProviderMissing`（`46001`）。装了包并在 `AddSmartAdmin()` 之前调用 `AddSmartAdminExcel()`，`TryAdd` 才轮到真实现。不装包，发布产物一个字节都不涨。接法见[给自己的实体接导入导出](/zh/guide/import-export)。

::: tip 实体住在 Services，不在 SqlSugar
数据层只提供 `IRepository<>` 和实体基类，具体的 `Sys*` 业务实体定义在 `SmartAdmin.Services`。原因是依赖方向：实体需要引用领域概念，而数据层不能反过来依赖领域层。
:::

::: warning 运行时依赖红线
核心包的第三方运行时依赖只有 SqlSugarCore + Microsoft.*。日志、雪花 ID 这些通常靠三方库（Serilog、Yitter.IdGenerator）的能力，内核都自带了单文件实现（`FileLoggerProvider`、`SnowflakeIdGenerator`），就是为了守住这条线。
:::

## 每层一个 `*Setup.cs`

每层的 DI 装配是一个静态扩展方法，命名一一对应：

- `SqlSugarSetup.AddSmartAdminSqlSugar()`：`backend/src/SmartAdmin.SqlSugar/SqlSugarSetup.cs`
- `ServicesSetup.AddSmartAdminServices()`：`backend/src/SmartAdmin.Services/ServicesSetup.cs`
- `SmartAdminSetup.AddSmartAdmin()`：`backend/src/SmartAdmin.AspNetCore/SmartAdminSetup.cs`

`AddSmartAdmin` 是组合根：它先绑定配置，再逐层向下调用。消费方看到的只有它。

```csharp
// 零配置基线:去掉 backend/samples/MinimalHost/Program.cs 里那几行可选包调用,剩下的就是最小宿主
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSmartAdmin(builder.Configuration);
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

## 组合根如何逐层向下

`AddSmartAdmin` 的装配次序（见 `SmartAdminSetup.cs`）：

1. **绑定配置**。先 `configuration.GetSection("SmartAdmin").Bind(options)`，再跑一遍可选的 `configure` 回调做覆写。最后把 `SmartAdminOptions` 及其各子节（`Database` / `Cache` / `Jwt` / `Security` / `Upload` / `Api` / `Id` / `Logging`）作为单例入容器。缺省即默认值，所以零配置可跑。
2. **雪花机器号校验**。选了 Redis 缓存却没显式给 `SmartAdmin:Id:WorkerId`，启动就直接抛错。选 Redis 通常意味着要跑多实例，这一抛，把一个静默的主键冲突换成了一条看得懂的启动错误。没配机器号的单机部署不受影响，它在首次解析 `AdminIdOptions` 时由文件锁自动抢号。为什么两个实例撞同一个 `WorkerId` 会撞主键，[数据层与审计](./data-layer.md)里雪花 ID 的位运算讲得更细。
3. **当前用户 + 数据范围环境**。HTTP 侧实现 `HttpContextCurrentUser`、`HttpContextDataScopeContext` 在此先 `TryAdd` 注册，压过 SqlSugar 层的 `AsyncLocal` 兜底实现。
4. **调用下层**。`AddSmartAdminSqlSugar(options.Database, entityAssemblies, options.AdditionalDatabases)` 装数据层（主库 + 可选副库），`AddSmartAdminServices()` 装领域服务。
5. **宿主集成**。JWT 密钥解析、认证/授权、MVC 控制器 + 全局过滤器、CORS、限流、OpenAPI、健康检查。

```csharp
// SmartAdminSetup.AddSmartAdmin 内,向下装配数据层与领域层
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddSmartAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);
services.AddSmartAdminServices();
```

每层其实都能独立装配。`AddSmartAdminSqlSugar` 是公开入口，不必等 `AddSmartAdmin` 整体登场，在裸容器上单独调用也行；测试用的就是这条路径，只装数据层，不带 JWT、控制器这些用不上的宿主集成。正因为调用方可能只是这样一个裸容器，它内部对可选依赖用的是 `GetService`，不是 `GetRequiredService`：没有日志工厂就静默不打，不会凭空多出一个必需依赖，把本该独立跑起来的数据层卡在起不来。

## 消费方的实体和控制器如何挂进来

消费方的业务程序集通过 `options.ApplicationAssemblies` 登记。这是代码侧设置，不从配置绑定：

```csharp
builder.Services.AddSmartAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyBusinessModule).Assembly);
});
```

登记后，这个程序集在组合根里走两条路：

- **实体参与 CodeFirst 建表**。组合根把内置 Services 程序集和消费方程序集合并成一份实体扫描源，传给 `AddSmartAdminSqlSugar`。消费方实体因此一并被 `DatabaseInitializer` 建表。
- **控制器挂入同一 MVC 管道**。组合根对每个消费方程序集做 `mvc.AddApplicationPart(assembly)`，消费方控制器与内置控制器走同一套过滤器（异常信封、操作日志、裸返回包装）、同一套认证授权。

```csharp
// 控制器:内置 + 消费方,同一 MVC 管道
var mvc = services.AddControllers(o => { /* 全局过滤器 */ })
    .AddApplicationPart(typeof(SmartAdminSetup).Assembly);   // 内置控制器
foreach (var assembly in options.ApplicationAssemblies.Distinct())
    mvc.AddApplicationPart(assembly);                        // 消费方控制器
```

::: warning 改这条路径要当心
在 `SmartAdminSetup` 里动实体扫描或控制器注册时，务必保住这两条挂载路径。一旦漏掉，消费方模块会静默失效：表建不出来，控制器 404，而且不报错。
:::

## 元包只是一个聚合入口

`SmartAdmin.csproj` 本身没有代码，只有一条 `ProjectReference` 指向 `SmartAdmin.AspNetCore`。消费方只装元包这一个，依赖传递就会拉起 AspNetCore → Services → SqlSugar → Core 整条栈。想要更细粒度的控制，比如只要数据层，直接装下层包也行。
