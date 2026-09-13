# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SmartAdmin is a **distributable admin-system kernel**, not an application. It ships as NuGet packages so a consumer gets a full enterprise back-office (auth, RBAC, multi-org data permissions, dict/config, logging, uploads) by calling `AddSmartAdmin` / `MapSmartAdmin` in `Program.cs`. The overriding design constraint is **replaceability**: every service is interface-backed, `virtual`, and registered via `TryAdd` so a consumer can swap any piece without forking. Runtime deps are **only SqlSugarCore + Microsoft.\*** — no other third-party frameworks in the core packages.

The repo holds the kernel's two halves, released together under one version number:
- `backend/` — the .NET 10 kernel (the product) + sample host + tests.
- `web/` — the Vue 3 + Naive UI frontend kernel as an npm workspace: `packages/admin` is the npm package `smart-admin-web` (layouts, routing, stores, shared components, every built-in page), and `template` is the thin app shell a consumer degits as its starting point (`npx degit SmartCode-X/SmartAdmin/web/template web`) and upgrades by bumping `smart-admin-web`.

Codebase comments and docs are in Chinese. The design rationale lives in `docs/rebuild-design.md`; code comments don't cite its section numbers, they state the reasoning directly. **Git commit messages are written in Chinese** in conventional-commit format (`type(scope): 主题`) — see `skills/write-commit.md` (`/write-commit`); `type` and `scope` stay lowercase English.

## Commands

Backend (run from repo root; solution is `.slnx`, not `.sln`):
```bash
dotnet build backend/SmartAdmin.slnx -c Release
dotnet test  backend/SmartAdmin.slnx                       # xUnit v3 + WebApplicationFactory, defaults to SQLite
dotnet test  backend/SmartAdmin.slnx -- --filter-class "*DataScopeTests*"   # single test/class
dotnet run   --project backend/samples/MinimalHost         # zero-config run on http://localhost:5100
```
**Tests run on Microsoft.Testing.Platform (MTP), not VSTest** — xunit v3 compiles the test project into an executable, which is why `SmartAdmin.Tests.csproj` sets `<OutputType>Exe</OutputType>` and the repo root carries a `global.json` with `test.runner: Microsoft.Testing.Platform`. MTP 2.x refuses to run under the VSTest target on the .NET 10 SDK, so neither is optional. Everything after `--` is the test executable's own command line: `--filter-class` / `--filter-method` / `--filter-namespace` (repeat for OR) instead of `--filter "FullyQualifiedName~…"`, `--max-threads N` instead of `xUnit.MaxParallelThreads=N`, `--report-xunit-trx --report-xunit-trx-filename x.trx` instead of `--logger "trx;…"`. (`xunit.v3`'s build props already flip the project to an executable; the explicit `OutputType` just makes that visible in the csproj.)
The test infrastructure (`TestDb`, `AdminAppFactory<TEntryPoint>`, `PostJson`/`ReadEnvelope`/`LoginToken`) lives in `backend/src/SmartAdmin.Testing` — a packable, non-runtime package shared with consumers and the `smart-app` template; `SmartAdmin.Tests` keeps only a thin `AdminAppFactory` subclass (disables the Dict module). It is not part of the `SmartAdmin` meta-package, and test projects must still reference `Microsoft.AspNetCore.Mvc.Testing` directly (its content-root targets don't flow transitively).
Tests against MySQL via env vars:
```bash
SMART_TEST_DBTYPE=MySql SMART_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/SmartAdmin.slnx
```

Frontend (run from `web/`, the workspace root):
```bash
npm run dev          # template app on :5173 with the package's source aliased in (HMR on kernel pages); proxies /api /openapi /hub to :5100 (override: SMART_API_TARGET)
npm run build        # package (vue-tsc + Vite library build → packages/admin/dist), then the template app
npm test             # vitest for the package
npm run lint         # oxlint (lint:fix to autofix)
npm run format:check # prettier, check-only (format to autofix)
npm run typecheck    # vue-tsc --noEmit, package + template
npm run gen:api      # regenerate packages/admin/src/api/schema.d.ts from a RUNNING backend's /openapi/v1.json (target: SMART_API_TARGET or --target, default :5100)
npm run gen:icons    # regenerate the offline icon subset packages/admin/src/assets/icons/ph-subset.json after adding a ph:* name (icons.spec.ts fails on a stale subset)
```

Full local env: `dev-start.bat` starts backend + frontend in separate windows (runs `npm install` in `web/` before Vite starts); `dev-stop.bat` stops them.

Package versions are **centrally managed** — add/bump deps in `backend/Directory.Packages.props` (`<PackageVersion>`), not in individual `.csproj` files. Shared build/NuGet metadata lives in `backend/Directory.Build.props`.

## Backend architecture

Layered package chain, dependencies pointing downward only: `SmartAdmin.Core` (contracts: interfaces, Options, `Result<T>`, `ErrorCode`, `AdminException` — no SqlSugar, no ASP.NET) → `SmartAdmin.SqlSugar` (data layer: `ISqlSugarClient` singleton, `IRepository<>`, entity base classes, CodeFirst, seed runner) → `SmartAdmin.Services` (domain: entities, `*Service` implementations, RBAC/data-scope providers, event bus) → `SmartAdmin.AspNetCore` (host integration: `AddSmartAdmin`/`MapSmartAdmin`, JWT, filters, built-in controllers), topped by the `SmartAdmin` meta-package. Each layer's DI wiring is a `*Setup.cs` extension; `AddSmartAdmin` (`SmartAdminSetup.cs`) is the composition root.

Repo-wide hard constraints: built-in services register with **`TryAdd*`** so a consumer's pre-registration wins (never plain `Add*`); consumer business assemblies wire in via `options.ApplicationAssemblies` (entities join CodeFirst, controllers get `AddApplicationPart`-ed — don't break this path, or consumer tables/controllers silently stop appearing); permission codes are normalized routes (`{METHOD}:/{route template}`), never string constants; errors are numeric `ErrorCode`s, never localized text. The replaceability contract (`ReplaceabilityTests`) locks the TryAdd / pre-registration-wins guarantee.

Full detail — request pipeline, data-layer conventions (global filters, audit AOP, snowflake WorkerId + `WorkerIdLeaseGuard`), zero-config bootstrap, multi ConfigId — lives in `backend/CLAUDE.md` (auto-loaded when working under `backend/`).

## Frontend architecture (`web/`)

Vue 3 `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse, shipped as the precompiled package `smart-admin-web`. Its public API is `web/packages/admin/src/index.ts` (apps import only from `'smart-admin-web'`); inside the package the alias is `#/` → `src/`, never `@/`. Singletons (vue, vue-router, pinia, vue-i18n, naive-ui, @vueuse/core, @iconify/vue, smart-naive-table, smart-naive-icon) are peerDependencies the app installs once. An app extends the kernel through `createSmartAdmin({ views, locales, routes, menuTitles, icons, iconSets, install, plugins, ... })` — kernel < plugins < app, the later layer wins on the same key, so the app always beats the kernel (the backend reaches the same outcome through `TryAdd`, where the consumer's earlier registration wins); a page's key is its path under `views/` without `.vue`, the same string as the menu's `component` field. API is contract-generated from the backend's OpenAPI (`npm run gen:api`); the menu tree and permission codes are fetched from the backend after login and injected as dynamic routes; `v-auth` (`directives/auth.ts`) gates buttons by permission code. Shared components are catalogued in `web/COMPONENTS.md` — read it before writing a page.

Full detail — package layout, public API surface, extension options, view registry, the two API-type layers, icons, routing internals, store responsibilities, tests — lives in `web/CLAUDE.md` (auto-loaded when working under `web/`).

## CI

GitHub Actions on `SmartCode-X/SmartAdmin` (`.github/workflows/`):

- `ci.yml` — push/PR to `main`/`dev` (docs-only changes are skipped). A `changes` job diffs the push/PR base and gates the halves: `backend` + `template-smoke` only when `backend/**`, `templates/**` or the workflow changed, `web` only when `web/**` changed, `web-e2e` when either half changed (no usable base, `schedule`, `workflow_dispatch` → run everything). `backend` is a `[sqlite, mysql, sqlserver, postgres]` matrix with `fail-fast: false` (the SqlServer leg stays on every `dev` push — it is the dialect most consumers run in production, so a dialect regression must surface before `main`); each leg starts **only its own** DB via `docker run` in the background while `dotnet build` runs (sqlite starts none), Redis is a service on every leg with `SMART_TEST_REDIS` set so the Redis contract tests really run (they fail under `GITHUB_ACTIONS` when it is missing — a silent skip is a false green), and tests run with `-- --max-threads 8` (2-vCPU runner, DB-round-trip-bound tests; local runs keep the CPU-count default). `template-smoke` runs `templates/smoke-test.ps1` (pack → local feed → `dotnet new smart-app` → build); `web` runs lint + `format:check` + vitest + build; `web-e2e` runs Playwright against MinimalHost + Vite on ports injected via `SMART_E2E_API_PORT`/`SMART_E2E_WEB_PORT` (locally: `ci.bat -Stage web-e2e`). `codeql` and `dependency-review` run only while the repo is public (they need GitHub Advanced Security); `deps-audit` (NuGet + npm advisories) runs unconditionally as the fallback when those two are gated off.
- `docker-smoke.yml` — `single` (image boots, creates tables, seeds, issues a token, then a Trivy scan reports HIGH/CRITICAL and gates on CRITICAL for both images) and `multi` (two replicas behind Caddy via `docker-compose.scale.yml` + `scripts/smoke-multi-replica.sh`: cross-replica force-logout, lockout threshold, cluster-wide rate limit, distinct `WorkerId`, real client IP, single scheduler leader with failover to the standby on leader kill). Keep `multi` separate — those guarantees only surface with two replicas.
- `release.yml` — on `v*` tags: verify (tag sits on `main`; CHANGELOG, the three `web/` `package.json` versions, the template's exact `smart-admin-web` dependency and the site badges agree; build with `-p:Version`, SQLite + Redis tests — skipped when the same SHA already has a green `backend (sqlite)` job in `ci` (build once, promote), template smoke with the real version, pack 13 nupkg, web vitest + `npm pack` of `smart-admin-web`, capture `openapi.json`) → publish (nuget.org **Trusted Publishing** via `NuGet/login`; npm **Trusted Publishing** of the verified tgz, re-published from its extracted folder with `--ignore-scripts`, skipped when the version is already on npm; no API key or npm token in the repo; GitHub Release with the CHANGELOG section, nupkgs and tgz attached). The nuget.org policy and the npm trusted publisher must both name the workflow file `release.yml`.
- `docs.yml` — triggers on `site/**` and `CHANGELOG.md` (it's `@include`-d into the changelog page): prose lint (selftest first) → `check:llms` → VitePress build → deploy to GitHub Pages on `main`. The deploy step is guarded on repo visibility, so it no-ops by itself if the repo ever goes private (a Free-plan private repo has no Pages).

**Run the gates locally — `ci.bat` (wraps `scripts/ci-local.ps1`).** CI is the authority; local is the inner loop. It answers in minutes without a push-and-wait round-trip, which is the whole point — the same failure found before the push costs one edit instead of a commit, a push, and a run to read. The script mirrors `ci.yml` + `docs.yml` where the verdict comes from — same `-warnaserror`, same SqlServer `--filter-class` subset, same connection strings and e2e ports — because a local runner that disagrees with CI is worse than none.

```bat
ci.bat                                                       backend(sqlite) + web + docs + template + audit, no Docker
ci.bat -Stage backend -Dialect mysql,postgres,sqlserver      dialect legs (starts/removes its own containers)
ci.bat -Stage all -Dialect sqlite,mysql,postgres,sqlserver   everything, before merging to main
ci.bat -Stage web-e2e                                        Playwright against a real MinimalHost
ci.bat -Stage docker-smoke                                   compose up → /health → login (the `single` leg; needs Docker)
```

Three deliberate differences from CI, each with a reason: xUnit parallelism stays at the CPU-count default (the `--max-threads 8` in `ci.yml` is tuned for a 2-vCPU runner); `npm ci` runs only when `node_modules` is missing (`-Clean` forces it); CodeQL / dependency-review run on CI only — they need GitHub's own analysis and advisory infrastructure, so the local script has no equivalent and doesn't pretend to. Redis is wired automatically (an existing 6379 is reused, otherwise a throwaway container), because `RedisCacheTests.SkipWithoutRedis()` only fails under `GITHUB_ACTIONS` — locally a skip and a real run both report the same passing count, so when Redis can't be reached the script says so under the summary table rather than leaving a silent false green.

**Database ports are chosen at runtime, not copied from `ci.yml`** — the one place where copying CI would be actively wrong. A CI runner is clean; a dev machine is not. This machine has SQL Server installed and listening on 1433, so a container published to `1433:1433` left `127.0.0.1,1433` pointing at the developer's own instance: 57 of 62 tests failed with `用户 'sa' 登录失败`. That was the lucky shape — a false red. Had the credentials matched (`root`/`root` and `postgres`/`postgres` are the defaults this suite uses), the run would instead have created and dropped ~200 databases inside a real development server. So `Get-FreePort` tries `port + 20000`, then `+ 21000`, then `+ 22000` when the default is taken, the connection string follows, and the summary says which port was used and that the local service was left alone.

**Dialect legs are much more expensive locally than on CI** (measured 2026-09-06, Windows + Docker Desktop/WSL2): the default set is 9 min wall clock, but the MySQL leg alone is **16 min 42 s for the same 876 tests that take 3–5 min on a Linux runner** — roughly 4× slower, and it's disk I/O in the VM, not CPU. So the everyday local loop is the default set (SQLite); run the dialect legs locally only when actually touching the data layer, and otherwise push and let CI run them — a Linux runner does the same work in a quarter of the time.

**Releases only happen on Actions** — `release.yml` is what packs and publishes to nuget.org and npm via Trusted Publishing, which swaps a GitHub-issued OIDC token for publish rights. There is no local fallback, by design: neither the repo nor the maintainer's machine holds a NuGet API key or npm token. The one exception is the npm package's very first version, published by hand because npm can only attach a trusted publisher to a package that already exists (`docs/releasing.md` §4).

`scripts/ci-local.ps1` **must keep its UTF-8 BOM**: Windows PowerShell 5.1 reads a BOM-less file as the ANSI codepage, which mangles the Chinese comments badly enough to break parsing (GBK swallows the following `}` as a trail byte). `templates/smoke-test.ps1` gets away without one only because it is pure ASCII.

The Redis contract tests skip locally when `SMART_TEST_REDIS` is unset; set it (or run a Redis) when touching `RedisCacheProvider`, because a silent skip is a false green.

**SqlServer is slow, not hung.** The full SqlServer suite takes 40–60 min (measured 2026-07-20: 2302–3514 s, all green) vs 3–5 min for the other three legs, so don't give it a short timeout or cancel it for "looking stuck". `TestDb` gives every test its own database, and on SQL Server ~85% of the per-database cost is `CodeFirst.InitTables` (~20 s/db: the 23 `CREATE TABLE`/`CREATE INDEX` statements each auto-commit their own log flush), ×~200 databases. tmpfs on `/var/opt/mssql`, a targeted `ClearPool`, `DBCC CLONEDATABASE`, and per-test `BACKUP`/`RESTORE` were all tried and measured as no-ops or worse — don't re-attempt without new evidence. So push/PR runs only the dialect-sensitive subset on the SqlServer leg (the `--filter` in `ci.yml`: CodeFirst nullable upgrade, production bootstrap, seed upgrade / id range, data-scope and soft-delete boolean-predicate filters, nvarchar Chinese CRUD, `JobClaimTests`, multi ConfigId) and the full SqlServer suite runs on the nightly schedule or via `workflow_dispatch` with `full-sqlserver`, since DB-agnostic logic is already covered by the other legs. Don't move the SqlServer leg off `dev` pushes: SQL Server is the dialect most consumers run in production, and dialect regressions have come from service-layer query code, not just the data layer. The nightly leg's `if:` reads repo visibility at runtime, so it runs on the public repo and stops on its own if the repo ever goes private. The 1.5× run-to-run spread means a single run cannot resolve anything smaller than a ~50% change.

## Agent skills

### Module scaffolding

Building a new module (entity / backend CRUD / frontend page / service replacement)? Start from `skills/README.md` — `skills/new-module.md` orchestrates the full flow (entity → backend → tests → `gen:api` → frontend → i18n → menu/permission wiring). Also exposed as slash commands (`/new-module`, `/create-entity`, `/create-crud-backend`, `/create-crud-frontend`, `/replace-service`, `/create-job`, `/create-page-variant`, `/smart-release`) via thin wrappers in `.claude/skills/`; the markdown files in `skills/` are the single source of truth. `.agents/skills/` and `.codex/skills/` are mirrors generated by `node scripts/gen-skill-wrappers.mjs` (edit `.claude/skills/` only; the script strips Claude-Code-only frontmatter such as `disable-model-invocation` / `argument-hint`, and `--check` runs in the local `docs` gate). `/smart-release` is user-invoked only — never enter the release flow on your own.

### Writing docs

Writing or editing any page under `site/`? Read `skills/write-docs.md` first (`/write-docs`) — voice, punctuation, openings, em-dash budget, and the zh-is-source/en-is-translation contract. Its machine-checkable half is enforced by `site/scripts/lint-prose.mjs` (`cd site && npm run lint:prose -- <page>`).

### Releasing

Shipping a SmartAdmin version (changelog, frontend + site badge bumps, merge to `main`, `v*` tag; the `release` workflow packs and publishes via Trusted Publishing)? Use `skills/smart-release.md` (`/smart-release`). Human runbook and cadence notes stay in `docs/releasing.md`. Versioning: the major is the .NET major the kernel targets (10.x ↔ .NET 10; the next major follows the next .NET LTS), minor = features, patch = fixes; all packages share one number, and breaking changes are batched into the .NET-major bump where possible (rule at the top of `CHANGELOG.md`).

### Issue tracker

Issues/PRDs live as GitHub issues in `SmartCode-X/SmartAdmin` (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five canonical roles, label string = role name (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` + `docs/adr/` at repo root, created lazily. See `docs/agents/domain.md`.
