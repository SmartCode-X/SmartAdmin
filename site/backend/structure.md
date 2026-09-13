# Project Structure & Startup

There are two projects under `tests/`, and only one of them runs tests. The other, `SmartAdmin.TestHost`, is a consumer in disguise: it registers itself into `options.ApplicationAssemblies` so that its entities, seeds and controllers all travel the consumer mounting path. Whether the kernel can really be consumed from outside is what it exists to prove.

The design *why* — dependency direction, replaceability, the request pipeline — is unpacked on the [Architecture](/backend/architecture) page.

## Solution layout

`backend/SmartAdmin.slnx` groups every project into three solution folders:

| Folder | Contents |
| --- | --- |
| `samples/` | `MinimalHost` — the zero-config sample host used for local dev and manual verification; `WorkerHost` — a sample host that runs scheduled-job dispatch as its own process |
| `src/` | The twelve shipped packages |
| `tests/` | `SmartAdmin.Tests` (the test suite) and `SmartAdmin.TestHost` (a minimal consumer host) |

`src/` holds the packages described in depth on the [Architecture](/backend/architecture) page — here's just enough to orient you:

| Package | One-line purpose |
| --- | --- |
| `SmartAdmin.Core` | Core contracts, zero runtime dependencies: `Result<T>`, `ErrorCode`, snowflake ID, security/extension-point interfaces |
| `SmartAdmin.SqlSugar` | Data layer: single SqlSugar instance, CodeFirst table creation, idempotent seeding, audit/soft-delete/data-scope global filters, generic repository |
| `SmartAdmin.Services` | Domain services: auth / RBAC / org / data scope / dict / config / logging / upload business services and entities |
| `SmartAdmin.AspNetCore` | Host integration: one-call `AddSmartAdmin`/`MapSmartAdmin` wiring, JWT auth, `[RolePermission]` authorization, built-in controllers and filters |
| `SmartAdmin` | Meta-package: installing this alone pulls in the whole kernel (AspNetCore + Services + SqlSugar + Core) |
| `SmartAdmin.Caching.Redis` | Optional: `StackExchange.Redis`-backed `ICacheProvider`, opt-in before `AddSmartAdmin()` |
| `SmartAdmin.Auth.WeCom` | Optional: `IExternalAuthProvider` for WeCom login (desktop QR, web authorization inside the client) |
| `SmartAdmin.Auth.DingTalk` | Optional: `IExternalAuthProvider` for DingTalk QR-code login |
| `SmartAdmin.Auth.GitHub` | Optional: `IExternalAuthProvider` for GitHub OAuth App login |
| `SmartAdmin.Auth.WeChat` | Optional: `IExternalAuthProvider` for WeChat Open Platform website-app QR login |
| `SmartAdmin.Excel` | Optional: xlsx read/write and templates with dropdowns, opt-in before `AddSmartAdmin()`; without it those endpoints return `46001` |
| `SmartAdmin.Testing` | Test-support package: `AdminAppFactory<TEntryPoint>` (throwaway database + fixed super-admin password and JWT key), the four-dialect `TestDb` (switched by `SMART_TEST_DBTYPE`, template-database cloning for speed), `PostJson` / `ReadEnvelope` / `LoginToken`. Used by the kernel's own tests and by the `Tests/` project `dotnet new smart-app` scaffolds; references no kernel package and isn't part of the `SmartAdmin` meta-package |

## Central package versioning

`Directory.Packages.props` sets `ManagePackageVersionsCentrally=true` — every `.csproj` references a package by name only (`<PackageReference Include="..." />`), and this single file pins the version. A few pins carry an explicit CVE rationale in their comment rather than just tracking upstream:

- `SQLitePCLRaw.bundle_e_sqlite3` is explicitly bumped to `3.0.3` because the version Microsoft.Data.Sqlite transitively pulls (2.1.10/2.1.11) hits a SQLite CVE (NU1903 GHSA-2m69-gcr7-jv3q); 3.0.x carries the fix.
- `Microsoft.OpenApi` is explicitly bumped to `2.7.5` because the version `Microsoft.AspNetCore.OpenApi` 10.0.9 transitively pulls (2.0.0) hits a high-severity CVE (NU1903 GHSA-v5pm-xwqc-g5wc, affecting 2.0.0-preview.11 through 2.7.4); 2.7.5 is the first patched version.
- `Microsoft.Extensions.DependencyInjection.Abstractions` is bumped to `10.0.5` because `StackExchange.Redis` 3.0.11's transitive dependency on `Logging.Abstractions` 10.0.5 requires `DI.Abstractions` ≥10.0.5 — without the bump the centralized 10.0.0 pin conflicts and NuGet reports an NU1605 downgrade.

`Directory.Build.props` sets the shared build and package metadata for every project:

- `TargetFramework` is `net10.0`, with `Nullable` and `ImplicitUsings` both enabled.
- `GenerateDocumentationFile` is on and `CS1591` is suppressed via `NoWarn` — packages ship with XML doc comments (part of the package's value for a consumer stepping into kernel source), but public members aren't forced to have one to avoid a wall of warnings.
- NuGet metadata is centralized here too: `Version` (a local-build placeholder, overridden at publish time via `-p:Version` from the release tag), `PackageLicenseExpression` (`Apache-2.0`), and `PackageTags` (`admin;rbac;sqlsugar;aspnetcore;scaffold;kernel`).
- SourceLink is wired via `PublishRepositoryUrl`/`EmbedUntrackedSources`/`IncludeSymbols` (`snupkg` format) so consumers can step into kernel source while debugging. `ContinuousIntegrationBuild` only turns on when `GITHUB_ACTIONS` is set — it normalizes embedded source paths, which would otherwise scramble local debugging.

::: tip `IsPackable` defaults to false
`Directory.Build.props` sets `IsPackable` to `false` by default; each package under `src/` opts back in explicitly. Sample and test projects inherit the default and are never packed.
:::

## Sample host

`backend/samples/MinimalHost/Program.cs` is the real bootstrap consumers copy from — this is an excerpt; the full file also wires up the four external-login packages (WeCom/DingTalk/GitHub/WeChat) and `AddSmartAdminExcel()`, see that file for the complete listing:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSmartAdminRedisCache(builder.Configuration);
builder.Services.AddSmartAdmin(builder.Configuration);
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

`AddSmartAdminRedisCache` is called here, ahead of `AddSmartAdmin()` — but it's a no-op unless `SmartAdmin:Cache:Provider` is set to `Redis` in configuration, so the zero-config experience (SQLite, in-process cache) is unchanged by its presence. The external-login packages and `AddSmartAdminExcel()` in the full file are no-ops the same way, absent their matching configuration; drop all of these optional-package calls and what's left is the truly zero-config minimal host.

Its `appsettings.json` keeps file logging off by default (`SmartAdmin:Logging:File:Enabled: false` — diagnostics rely on stdout collection instead) and sets standard ASP.NET Core log levels. `appsettings.Development.json.example` is the template for the gitignored `appsettings.Development.json`; it holds just the seed super-admin account name and an empty password (left blank so the kernel prints a random one on first startup). `Properties/launchSettings.json` pins the dev URL to `http://localhost:5100` with `ASPNETCORE_ENVIRONMENT=Development`.

## Test infrastructure

`backend/tests/` has two distinct projects:

- **`SmartAdmin.TestHost`** is a minimal *consumer* host, not part of the automated test suite — it registers itself via `options.ApplicationAssemblies.Add(typeof(Program).Assembly)` so its own entities, seed data, and controllers (`SampleWidget`, `SampleDoc`, `CustomDictController`) exercise the same consumer-mounting path `WebApplicationFactory<Program>`-driven tests hit. See [Building a Business Module](/guide/business-module) for what it demonstrates.
- **`SmartAdmin.Tests`** is the actual xUnit v3 suite, run via `dotnet test`.

The multi-database matrix lives in `SmartAdmin.Tests/TestDb.cs`. It reads `SMART_TEST_DBTYPE` (`MySql` / `SqlServer` / `PostgreSQL`; unset defaults to SQLite) plus a matching connection-string env var per engine (`SMART_TEST_MYSQL`, `SMART_TEST_SQLSERVER`, `SMART_TEST_POSTGRESQL`). For non-SQLite engines, each test run's database name is deterministically derived from an `identity` string via a SHA-256 hash (`smart_it_` + first 16 hex chars) — the same identity always maps to the same database, which supports idempotent "start against the same database twice" test cases. Because SqlSugar's CodeFirst only creates tables, not databases, `TestDb` creates and drops the database itself through raw `MySqlConnection`/`SqlConnection`/`NpgsqlConnection` calls to the server before SqlSugar ever touches it.

## Config section overview

Everything binds from the `SmartAdmin` section of `appsettings.json` into `SmartAdminOptions` (`backend/src/SmartAdmin.Core/Options/SmartAdminOptions.cs`):

| Property | Sub-options type | Example default |
| --- | --- | --- |
| `Database` | `AdminDatabaseOptions` | `DbType = "Sqlite"`, `ConnectionString = "Data Source=./data/SmartAdmin.db"`, `EnableCodeFirst = true`; optional `CodeFirstVersion` — when set, the CodeFirst scan only runs when the value changes; `AllowDestructiveSchemaChange = false` — dropping, narrowing or NOT NULL-ing columns refuses startup by default |
| `AdditionalDatabases` | `List<AdminDatabaseConnectionOptions>` | Empty by default: secondary multi-ConfigId list; see [Configure Multiple Databases](/guide/multi-database) |
| `Cache` | `AdminCacheOptions` | `Provider = "Memory"`, `KeyPrefix = "smart:"`, `PermissionMinutes = 20` |
| `Seed` | `AdminSeedOptions` | superadmin account/password seeding |
| `Jwt` | `AdminJwtOptions` | signing key/issuer/expiry |
| `Security` | `AdminSecurityOptions` | session concurrency policy |
| `Upload` | `AdminUploadOptions` | storage root, size cap, extension allowlist |
| `Excel` | `AdminExcelOptions` | row-count and file-size caps for import/export, see [Wire Import/Export on Your Entity](/guide/import-export) |
| `Email` | `AdminEmailOptions` | email channel: an empty `Host` resolves to the logging implementation, a configured one to `SmtpEmailSender`, see [Authentication & Security](/backend/auth-security) |
| `ExternalAuth` | `AdminExternalAuthOptions` | external login / SSO callback base URL and the built-in OIDC provider list, see [External Login](/backend/external-login) |
| `Api` | `AdminApiOptions` | disabled-module list |
| `Realtime` | `AdminRealtimeOptions` | realtime push toggle and hub path, off by default, see [Realtime Notifications](/backend/realtime) |
| `DemoMode` | `bool` | `false` — when `true`, only GET/HEAD/OPTIONS are allowed, all writes rejected with error code `41002` |
| `Id` | `AdminIdOptions` | `WorkerId` — `null` by default, claimed on this machine with a file lock at startup; must be set explicitly per instance when scaling across machines or containers. `WorkerIdLockDir` — the lock directory, a machine-level path by default |
| `Logging` | `AdminLoggingOptions` | file logging diagnostics, off by default |
| `Jobs` | `AdminJobsOptions` | whether this replica participates in scheduled-job dispatch, heartbeat and leader-election lease, the HTTP job SSRF fence, and the SQL job master switch, see [Scheduled Jobs](/guide/scheduled-jobs) |

`ApplicationAssemblies` is the one exception — a `List<Assembly>` set in code (as shown in the sample host and `TestHost` snippets above), not bound from configuration, since assembly references can't come from JSON.

With the structure mapped, the natural next step is to see how these packages assemble together — [Layered Architecture and Package Dependencies](/backend/architecture) starts from the dependency direction; the CLI commands for building and testing live in [Contributing](/community/contributing).
