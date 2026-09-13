# Deployment: Choose a Route, Then Clear the Security Baseline

You've already got it running locally via `dotnet new smart-app`, and now you need to ship it to a server. `npm run dev` works because the Vite dev server reverse-proxies `/api` and `/openapi` to the backend (`web/vite.config.ts`) — that proxy layer only exists during development. The build output `web/dist` is a pile of static files, and who hosts it and how it finds the backend are the two questions going live has to answer.

## Pick a hosting route

The four options differ on just two points: who hosts the frontend build, and whether frontend and backend are same-origin.

| Route | Who hosts the frontend | Same-origin | When to pick it |
|---|---|---|---|
| [Route A: Monolithic](/guide/deployment/route-a) | The backend process itself (`UseStaticFiles`) | Yes | One process, one port — least fuss for an internal system |
| [Route B: Reverse Proxy](/guide/deployment/route-b) | nginx / Caddy | Yes | You have a gateway already, or want Caddy to auto-issue TLS certs |
| [Route C: True Cross-Origin (CDN)](/guide/deployment/route-c) | CDN / separate domain | No | Frontend on a CDN — the only route that needs CORS |
| [Containers & Multi-Replica](/guide/deployment/docker) | Caddy in a container | Yes | Going to Docker / K8s, or scaling horizontally |

Same-origin (A, B) is the easy path: `web/dist` requests the backend same-origin by default (the `apiBase` that `main.ts` passes to `createSmartAdmin` comes from `VITE_API_BASE` and is an empty string when that isn't set, and paths already include `/api/v1`), so no CORS. Only Route C has frontend and backend on different origins, and only then do both sides need CORS configured.

The first step is the same for all four routes: build the frontend first:

```bash
cd web
npm ci
npm run build     # output goes to web/dist/
```

## The security baseline you must clear before going live

Whichever route you pick, none of the following can be dodged in production. For several of them the kernel "refuses to start unless satisfied," rather than running the process with the risk baked in.

| Setting | Why it must be dealt with |
|---|---|
| `SmartAdmin:Jwt:SecretKey` | Unset in production (any non-Development environment) **refuses to start** — it throws outright. Only the Development environment auto-generates a key to `./data/dev-jwt.key` and prints a warning. Production must configure it explicitly (a random string ≥32 bytes), and it must not enter version control — use an environment variable or a secrets manager. |
| `SmartAdmin:Database` | Defaults to SQLite `./data/SmartAdmin.db` (relative to the ContentRoot). For multiple instances or concurrent writes, switch to MySQL / SqlServer / PostgreSQL (change the two items `DbType` + `ConnectionString`). |
| `SmartAdmin:Id:WorkerId` | The snowflake generator's machine bit. On a single machine it can stay unset — a file lock claims a number at startup; across machines or containers every replica must differ (0–63), or same-millisecond issuance collides on the primary key. Configuring Redis without giving it explicitly refuses startup outright — see [Containers & Multi-Replica](/guide/deployment/docker) for the details. |
| `SmartAdmin:Upload:RootPath` | Defaults to `./wwwroot/upload`. Declare it as a data volume, or files are lost on redeploy; on Route A (backend also hosting the frontend) you must also move it out of `wwwroot`, or uploaded files get served anonymously by the static middleware — see [Route A's auth-bypass warning](/guide/deployment/route-a). |
| `SmartAdmin:Api:ForwardedHeaders` | Required behind any reverse proxy / load balancer. Without it the backend always sees the proxy's single IP: every user shares one rate-limit bucket, per-IP brute-force protection drops to zero, and the audit log's IP column is void. For config details see [Route B](/guide/deployment/route-b). |
| `SmartAdmin:Cache:Provider` | A single instance can leave it `Memory`. Multiple replicas must switch to `Redis`, or forced logout, permission revocation, and login lockouts fail to propagate between replicas — and once they fail, they fail for days. Changing this setting alone isn't enough: the host project also needs the `SmartAdmin.Caching.Redis` package installed, and a call to `AddSmartAdminRedisCache(builder.Configuration)` **before** `AddSmartAdmin()`. Miss either condition and it silently falls back to the in-process cache. See [Containers & Multi-Replica](/guide/deployment/docker) for the details. |

All of the above can go through environment variables, with double underscores for nesting (common in containerized deployments):

```bash
SmartAdmin__Jwt__SecretKey='...'
SmartAdmin__Database__DbType='MySql'
SmartAdmin__Database__ConnectionString='Server=db;Port=3306;Database=smart;User ID=...;Password=...'
SmartAdmin__Upload__RootPath='/data/upload'
```

One item outside the table: `SmartAdmin:Database:SlowSqlMillis` (the slow-SQL warning threshold, default `1000` ms): any statement taking longer than that is logged at `Warning` along with its SQL and parameters; failed SQL is always logged at `Error` (with statement and parameters), unaffected by this setting and with no switch to turn it off. To observe every statement, lower it (e.g. `1`), but in production that drowns the logs. The log category is `SmartAdmin.Sql` — tune its level on its own if you want.

## The production table-creation gate: first-time creation and upgrade columns

Production has a table-creation safety gate: when `ASPNETCORE_ENVIRONMENT=Production`, tables aren't created or altered automatically even with `EnableCodeFirst=true` (true by default) — a production database is usually maintained by hand by a DBA, and the app shouldn't `ALTER` it on its own. To let it through, turn this on explicitly:

```json
{ "SmartAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

It defaults to false and governs two things:

- **First deploy to production against an empty database**: the tables don't exist yet, so the seed has nowhere to write. Either turn this on temporarily to let it create the tables and write the seed (you can turn it off again once created), or have a DBA create the tables named in the startup error first, then start.
- **Adding columns on a kernel-version upgrade**: a new kernel version may add columns to its own tables (adding fields is routine; the kernel never drops or narrows columns). Either turn this on for this startup to let it fill them in, or have a DBA `ALTER TABLE ... ADD COLUMN` by hand for the tables and columns named in the error.

### The destructive-change gate: `AllowDestructiveSchemaChange`

CodeFirst does more than add columns to an existing table. SqlSugar drops any column the entity doesn't declare, narrows a length from 255 to 100 exactly as written, and flips nullable to NOT NULL — the data is simply gone. SQLite is the exception: its CodeFirst only adds columns. So before the scan actually runs, the kernel compares each entity's columns with what the database has and picks out the four kinds of difference that lose data: columns the entity doesn't declare, narrowed string lengths, nullable-to-NOT-NULL, and text swapped with a numeric or temporal type. Any hit refuses startup, and the error lists them as "table.column: change (database now → entity wants)":

```
SmartAdmin failed to start: CodeFirst would apply destructive schema changes; refused. Differences: sys_user.Remark: narrow(varchar(255) → 100); sys_user.LegacyCode: drop(varchar(32)). ...
```

Once you have checked the list (that column really should go, the narrowed column holds nothing longer), let it through for this one startup:

```json
{ "SmartAdmin": { "Database": { "AllowDestructiveSchemaChange": true } } }
```

Turn it back off once the changes have run. To keep CodeFirst away from columns a DBA added to a table, mark the entity `[SugarTable(IsDisabledDelete = true)]`. On SQLite these differences only produce one Warning line and never block; deal with them before moving to another database. The gate rides on the scan: with `CodeFirstVersion` set and unchanged the whole scan is skipped, the gate with it, so routine startups pay nothing. It only judges shapes that are certain to lose data; a database that can't report a length (`nvarchar(max)`, `text`) or a type whose mapping differs per dialect (`Guid`, binary) is let through — better to miss one than to block a database that runs fine today.

### Slow startups with many tables or a remote database: `CodeFirstVersion`

CodeFirst compares every table's column definitions on every startup. With hundreds of entities and a database on another host, that scan is most of the startup time. Set a version and the scan only runs when the version changes:

```json
{ "SmartAdmin": { "Database": { "CodeFirstVersion": "2026.09.06" } } }
```

After a successful table creation the value is recorded in `sys_schema_version`. On the next startup, if it is unchanged and every entity table exists, the whole scan is skipped. Bump it whenever you change an entity — a date is the easiest choice. Forgetting to bump after adding a table is covered (a missing table is still created); forgetting after changing a column is not (the column is not added), so bump it as part of the entity change. Leave it unset to keep scanning on every startup. The "CodeFirst 建表完成" startup log line carries the SQL count and elapsed time, so you can tell whether a slow start is table creation or something else.

::: tip Why evolved columns are nullable
Columns the kernel **adds to existing tables** always use a nullable database column (`IsNullable`). SQL Server cannot `ADD` a `NOT NULL` column without a default to a table that already has rows; after a nullable add, old rows are `NULL` and the read path treats that as the default (e.g. MFA flags become false, absolute expiry falls back to session `ExpiresAt`). New properties may use `T?`, but a released public property keeps its CLR type and locks the ORM's default-value mapping with a regression test. If a DBA adds the column by hand, prefer nullable too unless you also supply a `DEFAULT` and backfill existing rows.
:::

::: warning Not letting it through fails at startup by name — this is deliberate
Neither scenario starts up broken: an empty database with missing tables throws an error naming the tables (`...but the following tables the seed needs to write to don't exist: sys_schema_version, ...`), and an upgrade with missing columns throws an error naming the columns (`schema is behind the current entities; the following table is missing columns: sys_user(Avatar)`) — just take one of the two options it tells you. The reason it would rather blow up at startup is that letting it slide means the process comes up fine and only blows up at the driver layer's "column doesn't exist" the first time that table is queried — an error with no table name and no column name, leaving no one able to tell what to ALTER. The guard only checks for missing columns, not changes to type / length / nullability: a DBA who deliberately widened a `varchar` or added their own column won't be flagged.
:::

On the first seed write, if `SmartAdmin:Seed:AdminPassword` isn't explicitly configured, the console prints a random super-admin password once (16 characters, shown just that once) — be sure to keep it. To fix the account and password, configure it.

### How seed data is handled on upgrade

Seeding is insert-only by default (existence checked by primary key), so seed rows the kernel **adds** (a new menu, a new config item) flow into your database automatically after an upgrade — nothing to do. Rows the kernel **changes** (moving a permission button under a different page, adding an icon to a built-in module) are driven by the `sys_schema_version` version gate: once the kernel bumps the seed version, the next startup refreshes the built-in rows of the two structural tables — the menu tree and modules — back to the new shape, then writes the version number back.

::: tip Built-in menus: structure belongs to the kernel, appearance to you
An upgrade only refreshes the **structural columns** of built-in menus: parent, type, permission code, path, component, icon and module. The title, order, visibility and enabled flag you changed in the menu-management page stay as they are — a "File management" entry you hid does not come back. Menus you added yourself are unaffected. The module table (`sys_module`) is still refreshed as whole rows. The config center (`sys_config`) only refreshes display name, group, order and remark, never a value; dictionaries, users and role grants are your data — an upgrade doesn't touch a single row of it.
:::

## Post-go-live self-check

Three curls confirm the whole chain works:

```bash
curl https://<your-domain>/health         # Healthy: process alive
curl https://<your-domain>/health/ready   # Healthy: DB + cache both reachable
curl -i https://<your-domain>/api/v1/ping # 401: API routing works (this endpoint requires login)
```

`/health` and `/health/ready` have different semantics, so don't probe the wrong one: `/health` only checks whether the process itself is still responding (matching k8s's livenessProbe, process-level restart); `/health/ready` actually connects to the database and cache (matching readinessProbe, load-balancer node removal). To decide "can it take traffic," probe the latter.

Then open the frontend and log in once; getting a menu back means the JWT secret, database, and seed data all line up.

One last easy false alarm: a 404 on `/openapi/v1.json` in production is expected behavior, not something missing from the deployment. It's only mounted in the Development environment as the contract source for the frontend's `npm run gen:api`, not a production endpoint.

## Rolling back

There's no dedicated rollback script — rolling back means redeploying the previous version. A consumer points the NuGet package references and `smart-admin-web` back at the previous version number together, keeping both sides on the same number; a Docker deployment switches the image tag back and runs `docker compose up -d`. The database doesn't need to roll back with it: CodeFirst only adds columns, never drops or narrows them, so a column the older code doesn't know about just sits there unused.

What you genuinely can't undo is the publish step itself. Once the `release` workflow has run for a tag, the packages are on nuget.org and npmjs.com — nuget.org can unlist a package but never delete it, and a version number published to npm can never be used again. The full cadence is in the [changelog](/changelog) and the [release runbook](https://github.com/SmartCode-X/SmartAdmin/blob/main/docs/releasing.md). Rolling back rewinds the instance you deployed, not a package that's already out the door.
