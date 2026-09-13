# Layered Architecture and Package Dependencies

`backend/src` holds twelve NuGet packages (plus the `SmartAdmin.Templates` scaffold package at the repo root under `templates/`, thirteen in all at publish time). Five of them form the core chain, which may only depend downward — upper layers can reference lower ones, never the reverse. Reverse that direction in any one layer and both replaceability and the dependency boundary collapse together. Six hang off `Core` as optional side-branches outside that chain, and the remaining one is a test-support package that joins neither the chain nor the meta-package.

## The core chain — five packages

```text
SmartAdmin.Core        Pure contracts: interfaces (I*Provider, I*Service), Options, Result<T>, ErrorCode, AdminException.
   ↑                   No SqlSugar, no ASP.NET.
SmartAdmin.SqlSugar    Data layer: ISqlSugarClient singleton (SqlSugarScope), IRepository<>, entity base classes,
   ↑                   CodeFirst DatabaseInitializer, seed runner.
SmartAdmin.Services    Domain layer: entities (Sys*), *Service implementations, RBAC / data-scope providers, event bus.
   ↑                   Entities are defined here, not in the SqlSugar layer.
SmartAdmin.AspNetCore  Host integration: AddSmartAdmin / MapSmartAdmin, JWT, [RolePermission] / [ActiveSession]
                       filters, built-in controllers, envelope / exception / operation-log filters.

SmartAdmin             Meta-package: references AspNetCore only. Consumers install this one package and transitively pull in the whole stack.
```

Off to the side, all six optional packages depend only on `Core` — none of Core/SqlSugar/Services/AspNetCore reference any of them back:

```text
SmartAdmin.Caching.Redis   Optional: RedisCacheProvider (StackExchange.Redis-backed ICacheProvider), opt-in via
                            AddSmartAdminRedisCache(configuration) called *before* AddSmartAdmin().
SmartAdmin.Auth.WeCom      Optional: an IExternalAuthProvider implementation for WeCom login (desktop QR, web authorization inside the client).
SmartAdmin.Auth.DingTalk   Optional: an IExternalAuthProvider implementation for DingTalk QR-code login.
SmartAdmin.Auth.GitHub     Optional: an IExternalAuthProvider implementation for GitHub OAuth App login.
SmartAdmin.Auth.WeChat     Optional: an IExternalAuthProvider implementation for WeChat Open Platform website-app QR login.
SmartAdmin.Excel           Optional: xlsx read/write and template generation with dropdowns, opt-in via
                            AddSmartAdminExcel() called *before* AddSmartAdmin().
   ↑
SmartAdmin.Core
```

None of the four login packages carries a single third-party runtime dependency beyond Microsoft.* — the dependency red line holds for optional packages too, not just the core chain. One more package, `SmartAdmin.Testing`, sits outside this dependency graph entirely and references no kernel package at all — see [Project Structure](./structure.md) for where it fits.

Responsibilities and dependency direction per layer:

| Package | Responsibility | Depends on | Third-party runtime dependency |
| --- | --- | --- | --- |
| `SmartAdmin.Core` | Contracts, Options, `Result<T>`, `ErrorCode`, `AdminException`, `IIdGenerator` | None | Microsoft.* only |
| `SmartAdmin.SqlSugar` | `SqlSugarScope` singleton, `IRepository<>`, `BaseEntity`/`DataEntity`, CodeFirst, seeding | Core | SqlSugarCore |
| `SmartAdmin.Services` | `Sys*` entities, service implementations, RBAC, data scope, [event bus](/backend/event-bus) | SqlSugar, Core | SqlSugarCore |
| `SmartAdmin.AspNetCore` | JWT, authorization filters, built-in controllers, global filters, `AddSmartAdmin` | Services, SqlSugar, Core | Microsoft.AspNetCore.* |
| `SmartAdmin` (meta-package) | Aggregation entry point | AspNetCore | — |
| `SmartAdmin.Caching.Redis` (optional) | `RedisCacheProvider` — Redis-backed `ICacheProvider` | Core only | StackExchange.Redis |
| `SmartAdmin.Auth.WeCom` (optional) | `IExternalAuthProvider` for WeCom login (desktop QR, web authorization inside the client) | Core only | Microsoft.* only |
| `SmartAdmin.Auth.DingTalk` (optional) | `IExternalAuthProvider` for DingTalk QR-code login | Core only | Microsoft.* only |
| `SmartAdmin.Auth.GitHub` (optional) | `IExternalAuthProvider` for GitHub OAuth App login | Core only | Microsoft.* only |
| `SmartAdmin.Auth.WeChat` (optional) | `IExternalAuthProvider` for WeChat Open Platform website-app QR login | Core only | Microsoft.* only |
| `SmartAdmin.Excel` (optional) | `IExcelReader`/`IExcelWriter`/`IExcelTemplateBuilder` for xlsx | Core only | MiniExcel, DocumentFormat.OpenXml |
| `SmartAdmin.Testing` (test support, outside the meta-package) | `AdminAppFactory<TEntryPoint>`, the four-dialect `TestDb`, HTTP envelope helpers | References no kernel package (config keys and HTTP only) | `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Data.Sqlite`/`SqlClient`, plus `MySqlConnector` and `Npgsql` |

`SmartAdmin.Caching.Redis` doesn't introduce a new mechanism — it's the kernel's `TryAdd` replaceability, applied to the cache provider. A consumer calls `AddSmartAdminRedisCache(configuration)` before `AddSmartAdmin()`, which `TryAddSingleton`s a `RedisCacheProvider` that wins the race and replaces the kernel's default in-process `MemoryCacheProvider`. Skip the call, or don't set `SmartAdmin:Cache:Provider=Redis`, and the kernel's in-process default keeps working unchanged.

`SmartAdmin.Excel` takes the same route: the three codecs the kernel registers by default are all `MissingExcelProvider`, and the first call throws `ErrorCode.ExcelProviderMissing` (`46001`). Install the package and call `AddSmartAdminExcel()` before `AddSmartAdmin()`, and `TryAdd` lands on the real implementations instead. Skip the package and the publish output doesn't grow by a byte. Wiring it up: [Wire Import/Export on Your Entity](/guide/import-export).

::: tip Entities live in Services, not in SqlSugar
The data layer only provides `IRepository<>` and entity base classes; the concrete `Sys*` business entities are defined in `SmartAdmin.Services`. This follows from the dependency direction: entities need to reference domain concepts, and the data layer cannot depend upward on the domain layer.
:::

::: warning Runtime dependency red line
The core packages' only third-party runtime dependencies are SqlSugarCore + Microsoft.*. Capabilities that are usually pulled from third-party libraries — logging, snowflake IDs (typically Serilog, Yitter.IdGenerator) — instead ship as single-file implementations inside the kernel (`FileLoggerProvider`, `SnowflakeIdGenerator`), precisely to hold this line.
:::

## One `*Setup.cs` per layer

Each layer's DI wiring is a static extension method, named to match the layer:

- `SqlSugarSetup.AddSmartAdminSqlSugar()` — `backend/src/SmartAdmin.SqlSugar/SqlSugarSetup.cs`
- `ServicesSetup.AddSmartAdminServices()` — `backend/src/SmartAdmin.Services/ServicesSetup.cs`
- `SmartAdminSetup.AddSmartAdmin()` — `backend/src/SmartAdmin.AspNetCore/SmartAdminSetup.cs`

`AddSmartAdmin` is the composition root: it binds configuration first, then calls down through each layer. It's the only thing consumers see.

```csharp
// The zero-config baseline: strip the optional-package calls out of backend/samples/MinimalHost/Program.cs and this is what's left
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSmartAdmin(builder.Configuration);
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

## How the composition root calls down through the layers

`AddSmartAdmin`'s assembly order (see `SmartAdminSetup.cs`):

1. **Bind configuration.** `configuration.GetSection("SmartAdmin").Bind(options)`, then run the optional `configure` callback to override, then register `SmartAdminOptions` and its sub-sections (`Database` / `Cache` / `Jwt` / `Security` / `Upload` / `Api` / `Id` / `Logging`) as singletons in the container. Everything defaults, so zero-config startup works.
2. **Validate the snowflake worker ID.** If Redis caching is chosen (implying multiple instances) but `SmartAdmin:Id:WorkerId` isn't set explicitly, startup throws immediately — turning a silent primary-key collision into a readable startup error. A single-machine deployment with no worker ID configured is unaffected: it claims one with a file lock the first time `AdminIdOptions` is resolved. For why two instances sharing a `WorkerId` actually collide on the primary key, the snowflake ID's bit layout is spelled out in [Data Layer and Auditing](./data-layer.md).
3. **Current-user + data-scope context.** The HTTP-side implementations `HttpContextCurrentUser` and `HttpContextDataScopeContext` are `TryAdd`-registered here first, taking precedence over the `AsyncLocal`-based fallback in the SqlSugar layer.
4. **Call down into lower layers.** `AddSmartAdminSqlSugar(options.Database, entityAssemblies, options.AdditionalDatabases)` wires the data layer (main DB plus optional secondaries), `AddSmartAdminServices()` wires the domain services.
5. **Host integration.** JWT key resolution, authentication/authorization, MVC controllers + global filters, CORS, rate limiting, OpenAPI, health checks.

```csharp
// Inside SmartAdminSetup.AddSmartAdmin, wiring the data and domain layers below it
var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
entityAssemblies.AddRange(options.ApplicationAssemblies);
services.AddSmartAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);
services.AddSmartAdminServices();
```

Incidentally, each layer can be assembled independently: `AddSmartAdminSqlSugar` is a public entry point, callable on its own against a bare container (used by tests, and by consumers who only need the data layer). Because of this, it resolves optional dependencies with `GetService` rather than `GetRequiredService` internally — no logger factory means it silently doesn't log, rather than turning into a required dependency that prevents startup.

## How a consumer's entities and controllers plug in

A consumer's business assembly is registered via `options.ApplicationAssemblies` (set in code, not bound from configuration):

```csharp
builder.Services.AddSmartAdmin(builder.Configuration, options =>
{
    options.ApplicationAssemblies.Add(typeof(MyBusinessModule).Assembly);
});
```

Once registered, this assembly takes two paths through the composition root:

- **Entities join CodeFirst table creation.** The composition root merges the built-in Services assembly with consumer assemblies into the entity-scanning source passed to `AddSmartAdminSqlSugar`, so consumer entities get tables created by `DatabaseInitializer` alongside the built-in ones.
- **Controllers join the same MVC pipeline.** The composition root calls `mvc.AddApplicationPart(assembly)` for each consumer assembly, so consumer controllers go through the same filters (exception envelope, operation logging, bare-return wrapping) and the same authentication/authorization as built-in controllers.

```csharp
// Controllers: built-in + consumer, same MVC pipeline
var mvc = services.AddControllers(o => { /* global filters */ })
    .AddApplicationPart(typeof(SmartAdminSetup).Assembly);   // built-in controllers
foreach (var assembly in options.ApplicationAssemblies.Distinct())
    mvc.AddApplicationPart(assembly);                        // consumer controllers
```

::: warning Handle this path with care
When touching entity scanning or controller registration in `SmartAdminSetup`, make sure both of these mounting paths stay intact. Drop either one and consumer modules silently break: their tables don't get created, their controllers 404 — with no error raised.
:::

## The meta-package is just an aggregation entry point

`SmartAdmin.csproj` itself has no code — just a single `ProjectReference` pointing at `SmartAdmin.AspNetCore`. A consumer installing the meta-package alone transitively pulls in the whole stack: AspNetCore → Services → SqlSugar → Core. For finer-grained control (e.g. needing only the data layer), a consumer can install a lower-layer package directly.
