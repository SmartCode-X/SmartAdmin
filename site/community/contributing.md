# Contributing Guide

A PR opened against `main` gets sent back: `main` only receives release merges, and day-to-day work goes to `dev`. There are a few rules like that one — none of them live in the code, and you meet them by tripping over them.

## Before you start

- Fork the repo and clone it locally.
- **Development happens on the `dev` branch; `main` only accepts release merges** — target your PR at `dev`, not `main`. `dev` is merged into `main` and tagged only at release time (see [CHANGELOG.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)).
- File bugs / feature requests through one of the three GitHub Issue templates (Bug report / Feature request / Question) — the repo has blank issues disabled. **Do not** open a public issue for a security vulnerability; see "Security issues" below.

## Local development environment

SmartAdmin has two halves: `backend/` (.NET 10 kernel + sample host + tests) and `web/` (the Vue 3 + Naive UI frontend), and you can change either independently or both together. `web/` is an npm workspace. `packages/admin` is the source of the frontend package `smart-admin-web`, which uses `#/` to point at its own `src/`. `template` is the thin shell app: in dev it runs straight off the kernel source with hot reload, and the e2e suite runs against it too.

Backend (run from the repo root; the solution file is `.slnx`, not `.sln`):

```bash
dotnet build backend/SmartAdmin.slnx -c Release
dotnet test  backend/SmartAdmin.slnx                       # xUnit v3 + WebApplicationFactory, defaults to SQLite
dotnet test  backend/SmartAdmin.slnx -- --filter-class "*DataScopeTests*"   # run a single test class
dotnet run   --project backend/samples/MinimalHost         # zero-config run, http://localhost:5100
```

The suite runs on Microsoft.Testing.Platform, and everything after `--` goes straight to the test executable itself, so filtering is `--filter-class` (repeatable, OR'd together) rather than the VSTest `--filter "FullyQualifiedName~..."`.

Running tests against MySQL (matches one leg of the CI matrix):

```bash
SMART_TEST_DBTYPE=MySql SMART_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/SmartAdmin.slnx
```

Frontend (run from the `web/` directory):

```bash
npm run dev           # the template's Vite, :5173, proxies /api, /openapi and /hub to backend :5100 (override with SMART_API_TARGET)
npm run build         # builds the package, then the template; each runs vue-tsc --noEmit && vite build
npm run lint          # oxlint (lint:fix to autofix)
npm run format:check  # prettier, check only (format to autofix)
npm test              # vitest, the package's unit tests
npm run typecheck     # vue-tsc --noEmit, package and template
npm run test:e2e      # Playwright, starts its own backend and template
npm run gen:api       # regenerate the package's schema.d.ts from a running backend's /openapi/v1.json
npm run gen:icons     # regenerate the offline icon subset after the package uses a new ph:* icon (a unit test catches a stale subset)
```

If running both sides separately is a hassle, `dev-start.bat` at the repo root launches backend + frontend together in two separate windows (running `npm install` in `web/` before Vite starts); `dev-stop.bat` stops them.

::: warning Don't hand-edit schema.d.ts
`web/packages/admin/src/api/schema.d.ts` is a contract file generated from the backend's OpenAPI, exported by the package as `KernelPaths`. If you change an endpoint, run `npm run gen:api` first (requires the backend to be running) — don't hand-write this file.
:::

## Centralized package versioning

Backend dependency versions are all collected in [`backend/Directory.Packages.props`](https://github.com/SmartCode-X/SmartAdmin/blob/main/backend/Directory.Packages.props)'s `<PackageVersion>` — add or bump dependencies there, **not** by pinning a version in an individual `.csproj`. Shared build/NuGet metadata (author, repo URL, license, etc.) lives in `backend/Directory.Build.props`.

## Commit messages: Chinese Conventional Commits

Code, comments, docs and git commits are all in Chinese, formatted as `type(scope): 主题`. `type` and `scope` stay lowercase English, because the release tooling parses them against a fixed vocabulary:

```text
fix(web): 无权限用户的按钮不再渲染
feat(backend): 新增定向通知投递
docs: 根目录配置与脚本文件的注释翻译
refactor(services): 登录流程拆成可覆写的虚方法步骤
```

Common `type` values: `feat` / `fix` / `docs` / `refactor` / `test` / `chore`. `scope` is usually `web` / `backend`, or a more specific module name. The full convention is in [Commit Convention](/standard/commit).

## Running tests: both legs need to be green

CI runs on every PR (the four-database backend matrix, the frontend, the template smoke test); running the checks locally first saves a round trip. Before touching `backend/**`, run `dotnet build backend/SmartAdmin.slnx -c Release` and `dotnet test backend/SmartAdmin.slnx`. The default leg uses SQLite, and `TestDb.cs` derives an isolated database per test from env vars like `SMART_TEST_DBTYPE`, so tests don't interfere with each other; when you touch the data layer, run the MySQL leg too by setting `SMART_TEST_DBTYPE=MySql` and a `SMART_TEST_MYSQL` connection string. The Redis contract tests in `RedisCacheTests` skip silently unless `SMART_TEST_REDIS` points at a Redis, so set it when you change caching.

For `web/**`, run `npm run lint` → `npm run format:check` → `npm test` (vitest) → `npm run build` (build already includes `vue-tsc` type checking, so there's no need to run `typecheck` separately) — the same checks as CI's frontend job. If you change `templates/**`, also run `pwsh templates/smoke-test.ps1`: it packs the kernel into a local feed, scaffolds `dotnet new smart-app` against it, then builds and runs the result — the first command a consumer runs after getting the package.

::: tip The replaceability contract tests are a contract, not an ordinary test
`ReplaceabilityTests` locks in the replaceability guarantees around TryAdd coverage, virtual-method overriding, and business-assembly mounting. For the full, current list of exactly what it guarantees, see [The Replaceability Model](/backend/replaceability). When you change DI registration or `SmartAdminSetup`-related code and this suite goes red, it usually means you've broken a consumer's replacement path — don't bypass or delete the tests; figure out which guarantee got broken first.
:::

## PR workflow

1. Branch off `dev` for your feature.
2. Keep each change focused on one thing; follow the commit conventions above.
3. Run the build/test/lint for the relevant side locally.
4. Open a PR targeting `dev`; CI runs the checks for you, so just say in the description which ones you ran locally.
5. If you're using Claude Code or another AI agent to help develop, the repo has conventions for issue triage, domain docs, and business-development skills — see [Agent Skills and AI-Assisted Development](./agent-skills).

## Security issues

**Do not report security vulnerabilities through a public issue.** SmartAdmin distributes as NuGet and npm packages with built-in auth, RBAC, and multi-org data permissions — a public report would disclose a 0-day to every downstream consumer before a patch exists.

Please use GitHub's private vulnerability reporting instead: [open a security advisory](https://github.com/SmartCode-X/SmartAdmin/security/advisories/new), visible to maintainers only. Maintainers will respond within 7 days and coordinate the fix and disclosure timeline with you. See [SECURITY.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/SECURITY.md) for details.

## License

SmartAdmin is open-sourced under the [Apache License 2.0](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE); code you submit is contributed under the same license by default.
