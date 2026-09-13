## Backend architecture

**Package layering** (dependencies point downward only; this ordering is the load-bearing constraint):

```
SmartAdmin.Core        contracts only: interfaces (I*Provider, I*Service surface), Options, Result<T>, ErrorCode, AdminException. No SqlSugar, no ASP.NET.
   ↑
SmartAdmin.SqlSugar    data layer: ISqlSugarClient singleton (SqlSugarScope), IRepository<>, entity base classes, CodeFirst DatabaseInitializer, seed runner.
   ↑
SmartAdmin.Services    domain: entities (Sys*), *Service implementations, RBAC/data-scope providers, event bus. Entities live HERE, not in SqlSugar.
   ↑
SmartAdmin.AspNetCore  host integration: AddSmartAdmin/MapSmartAdmin, JWT, [RolePermission]/[ActiveSession] filters, built-in Controllers, envelope/exception/oplog filters.

SmartAdmin             meta-package: references AspNetCore only; consumers install this one to pull the whole stack.
```

Each layer's DI wiring is a `*Setup.cs` extension (`SqlSugarSetup`, `ServicesSetup`, `SmartAdminSetup`). `AddSmartAdmin` (in `SmartAdminSetup.cs`, the composition root) binds config, then calls down the chain.

**Replaceability model** (the whole point — respect it when adding features):
- Built-in services are registered with **`TryAdd*`** so a consumer registering the same interface *before* `AddSmartAdmin()` wins. Never use plain `Add*` for a replaceable service.
- Long service methods are split into small `virtual` steps (template-method) so consumers override one step by subclassing, not by copying the method.
- Consumer business assemblies are wired in via `options.ApplicationAssemblies`: their entities join CodeFirst table creation and their controllers get `AddApplicationPart`-ed. When touching entity scanning or controller registration in `SmartAdminSetup`, keep this path intact — dropping it silently breaks consumer modules (their tables aren't created, their controllers 404).
- The replaceability guarantees are locked by the replaceability contract (`ReplaceabilityTests`, extension points listed in `ReplaceabilityContract.cs`) — treat it as a contract, not ordinary tests.

**Request pipeline** (an authenticated call flows through these, in order):
1. **Auth** — Microsoft JWT Bearer. Claims are unmapped (`sub`, `sid`, `sadm`, `unique_name`). Framework 401 challenges are reshaped into the standard envelope (code 40006).
2. **`[RolePermission]`** (`RolePermissionAttribute`) — permission code IS the normalized route: `{METHOD}:/{route template}` (e.g. `GET:/api/v1/ping`). There are **no permission strings in code** — authorization is granted by checking routes in the role-menu UI. Super admin (`sadm` claim) bypasses. Also validates the session (`sid`) is still active, so force-logout takes effect immediately. Use `[ActiveSession]` for any-logged-in-user endpoints that need no specific permission.
3. **Data scope** — during authorization, the user's effective org data-scope is resolved (cached) into `IDataScopeContext` (an `HttpContext.Items` carrier on the HTTP path — deliberately *not* `AsyncLocal`, which doesn't flow back through auth filters).
4. **Result envelope** — controllers may `return dto` directly; `ResultEnvelopeFilter` wraps bare returns into `Result<T>`. Business errors are thrown as `AdminException` / returned as `ErrorCode` and turned into envelopes by `AdminExceptionFilter`. **Errors are numeric `ErrorCode`s, never localized text** — the envelope carries the code plus its `msgKey`, and the frontend translates the `msgKey` (`docs/rebuild-design.md` §13.2).

**Data layer conventions** (enforced globally in `SqlSugarSetup`, so business code stays clean):
- One `SqlSugarScope` singleton (thread-safe). Global query filters: soft-delete (`ISoftDelete` → `IsDelete == false`) and **data scope** (`IOrgScoped`/`DataEntity` filtered by the current request's resolved org set). The data-scope filter is the signature feature (`§6`).
- AOP auto-fills audit fields on insert/update: snowflake `Id` (when 0), `CreateTime`, `CreateUserId`, `CreateOrgId` (the data-scope anchor — if this isn't filled, org-scoped queries return 0 rows), `UpdateTime`, `UpdateUserId`. Business code sets business fields only.
- Snowflake `WorkerId`: an explicit `SmartAdmin:Id:WorkerId` is used as-is; when unset, `WorkerIdLease` (Core) claims a free slot on this machine with an exclusive file lock (default dir `%ProgramData%\SmartAdmin\workerid` on Windows, `/tmp/smartadmin/workerid` elsewhere; override `SmartAdmin:Id:WorkerIdLockDir`) and writes it back into `AdminIdOptions` on first resolution, so same-machine multi-process (IIS overlapping recycle, web garden) never shares a number. File locks don't cross machines/containers — there it **must be set explicitly per instance** or same-millisecond IDs collide. Read the effective id via DI `AdminIdOptions`, never `SmartAdminOptions.Id` directly. `WorkerIdLeaseGuard` backs this with a DB lease in `sys_worker_lease`, and its node name is deliberately split: `{machine}#{workerId}` before the `@` is the **stable identity** that decides whether the number is taken, the token after it changes every startup and is only the CAS condition for renew/release. Same machine, same number, previous pid gone → take the lease over immediately, don't wait out the TTL; comparing the whole node name instead means a restarted process cannot recognize its own leftover row, so every hard kill (stop debugging, closed console, container restart landing on the same pid) costs a full TTL before the app can start. Only "another live process on this machine" and "another machine" throw. The identity (machine name / pid / liveness probe) is a constructor parameter precisely so `WorkerIdLeaseGuardTests` can simulate a second process — keep that seam when editing.

**Zero-config bootstrap**: default SQLite (relative paths resolved against ContentRoot), CodeFirst auto-DDL via `DatabaseInitializer` (a hosted service), seed data (`ISeedData` implementations run once, idempotently), and a **random super-admin password printed to the console on first startup**. Switch dialect by changing `SmartAdmin:Database` (DbType + connection string); SQLite/MySQL/SqlServer/PostgreSQL supported. Same-process **extra connections** (multi ConfigId): `SmartAdmin:AdditionalDatabases` — see site guide `site/zh/guide/multi-database.md` (en: `site/guide/multi-database.md`); access via `db.AsTenant().GetConnection(configId)`; `IRepository<>` always hits main.

Config lives under the `SmartAdmin` section of `appsettings.json`, bound to `SmartAdminOptions` (see `Core/Options/*`). `appsettings.Development.json` is gitignored (holds credentials) — copy from the `.example`.

Health/OpenAPI: `/health` (liveness), `/health/ready` (DB+cache), and `/openapi/v1.json` (dev-only, the frontend's contract source).
