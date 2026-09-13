# Core Concepts

Calling `AddSmartAdmin` and `MapSmartAdmin` in `Program.cs` buys a complete enterprise back office: auth, RBAC, multi-org data permissions, dict and config, logging, uploads, notices and announcements, a read-only demo mode, a password-expiry policy. The backend arrives as NuGet packages and the frontend as the npm package `smart-admin-web` — a **kernel**, not an application, with your business code staying in your own repository. That shape forces one constraint on everything inside it: every piece has to be replaceable.

## Why not just another admin template

Copying an admin template gets you started fast, but as business code grows the project ends up deeply coupled to the template — and after that, upgrading base capabilities, pulling in upstream changes, or swapping out just one piece all become painful.

SmartAdmin factors these common capabilities out of business code: you can use the default implementations as-is, integrate it fairly naturally into an existing project, or replace any single piece without forking.

## The replaceability model

It comes down to three constraints, locked in by the replaceability contract (`ReplaceabilityTests`):

1. **Interface registration + `TryAdd`** — built-in services are all registered with `TryAdd*`, so a consumer registering the same interface before `AddSmartAdmin()` wins and overrides the default implementation.
2. **Template-method decomposition** — long service methods are split into small `virtual` steps, so a consumer overrides **one step** via subclassing instead of copying the whole method.
3. **Business assembly mounting** — a consumer's entities join CodeFirst table creation via `options.ApplicationAssemblies`, and their controllers get `AddApplicationPart`-ed automatically, extending the system without touching the kernel.

What these three look like in actual code is in [Replace Built-in Services](/guide/replace-service).

The frontend reaches the same outcome by the opposite mechanism: `createSmartAdmin` registers pages and text in the order kernel, plugins, app, and on the same key the later registration wins, so an app page with the same key as a built-in page simply takes its place. How to plug in is covered in [The Frontend Template](/guide/frontend-templates).

## Package layering

Dependencies point downward only — this ordering is itself a load-bearing constraint:

```text
SmartAdmin.Core        Pure contracts: interfaces, Options, Result<T>, ErrorCode. No SqlSugar, no ASP.NET.
   ↑
SmartAdmin.SqlSugar    Data layer: ISqlSugarClient singleton, IRepository<>, entity base classes, CodeFirst, seeding.
   ↑
SmartAdmin.Services    Domain layer: entities (Sys*), service implementations, RBAC / data scope.
   ↑
SmartAdmin.AspNetCore  Host integration: AddSmartAdmin / MapSmartAdmin, JWT, permission/session filters, built-in controllers.

SmartAdmin             Meta-package: references AspNetCore only; a consumer installs this one to pull in the whole stack.
```

## Request pipeline

An authenticated request flows through, in order:

1. **Authentication** — Microsoft JWT Bearer; the framework's 401 is reshaped into the standard envelope (code 40006).
2. **`[RolePermission]`** — the permission code IS the normalized route (`{METHOD}:/{route}` — the `GET:/api/v1/ping` call from the previous page is one); **there are no permission strings in code** — authorization is granted by checking routes in the role-menu UI. Super admin (`sadm`) bypasses directly, while session validity is also checked (so a forced logout takes effect immediately).
3. **Data scope** — during authorization, the current user's effective org data scope is resolved and injected into `IDataScopeContext`.
4. **Result envelope** — controllers can `return dto` directly, and a filter wraps it into `Result<T>`; business errors are thrown as `AdminException` / returned as `ErrorCode` and turned into an envelope. **Errors are numeric `ErrorCode`s, never localized text** — i18n is handled on the frontend by translating the code.

## Data layer conventions

- A single `SqlSugarScope` singleton; global query filters automatically apply **soft delete** (`ISoftDelete`) and **data scope** (`IOrgScoped` / `DataEntity` filtered by the org set resolved for the current request).
- AOP auto-fills audit fields on insert/update: snowflake `Id`, `CreateTime`, `CreateUserId`, `CreateOrgId` (the data-scope anchor), `UpdateTime`, `UpdateUserId`. Business code only needs to set business fields.
- The snowflake `WorkerId`, when unset, is claimed on this machine with a file lock, so processes on one box never share a number. **When scaling across machines or containers it must be set explicitly and differ per instance**, or IDs generated in the same millisecond will collide.

---

> For a more complete picture of the architecture and design rationale, see the repo's [Architecture & Design Document](https://github.com/SmartCode-X/SmartAdmin/blob/main/docs/rebuild-design.md).
