# 贡献指南

对着 `main` 开的 PR 会被请回去重开：`main` 只接收发版合并，日常开发一律进 `dev`。这类约束还有几条，都不写在代码里，撞上了才知道。

## 开始之前

- Fork 仓库，clone 到本地。
- **提 PR 请对准 `dev`，不要对准 `main`**：日常开发都在 `dev` 上进行，发版时才会把它合进 `main` 再打 tag（见 [CHANGELOG.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)）。
- 报 bug / 提需求走 GitHub Issues 的三个模板（Bug report / Feature request / Question），仓库关闭了空白 issue。安全漏洞不要开公开 issue。

## 本地开发环境

SmartAdmin 分两半：`backend/`（.NET 10 内核 + 示例宿主 + 测试）和 `web/`（Vue 3 + Naive UI 前端），两半可以独立改动，也可以一起改。`web/` 是一个 npm 工作区。`packages/admin` 是前端包 `smart-admin-web` 的源码，包内用 `#/` 指向自己的 `src/`。`template` 是薄壳应用，dev 时直接走内核源码、改了就热更新，e2e 也跑在它上面。

后端（仓库根目录跑；解决方案文件是 `.slnx`，不是 `.sln`）：

```bash
dotnet build backend/SmartAdmin.slnx -c Release
dotnet test  backend/SmartAdmin.slnx                       # xUnit v3 + WebApplicationFactory,默认 SQLite
dotnet test  backend/SmartAdmin.slnx -- --filter-class "*DataScopeTests*"   # 只跑某个测试类
dotnet run   --project backend/samples/MinimalHost         # 零配置起服,http://localhost:5100
```

测试跑在 Microsoft.Testing.Platform 上，`--` 之后的参数原样交给测试程序自己，所以过滤用 `--filter-class`（可重复，之间是「或」），不是 VSTest 那套 `--filter "FullyQualifiedName~..."`。

针对 MySQL 跑测试（对应 CI 矩阵里的一条腿）：

```bash
SMART_TEST_DBTYPE=MySql SMART_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/SmartAdmin.slnx
```

前端（`web/` 目录下跑）：

```bash
npm run dev           # 模板的 Vite,:5173,代理 /api、/openapi、/hub 到后端 :5100(可用 SMART_API_TARGET 覆盖)
npm run build         # 先构建包再构建模板,各自 vue-tsc --noEmit && vite build
npm run lint          # oxlint(lint:fix 自动修复)
npm run format:check  # prettier 只检查(format 自动修)
npm test              # vitest,包的单测
npm run typecheck     # vue-tsc --noEmit,包和模板
npm run test:e2e      # Playwright,自己拉起后端和模板
npm run gen:api       # 从一个正在运行的后端的 /openapi/v1.json 重新生成包的 schema.d.ts
npm run gen:icons     # 包里用了新的 ph:* 图标后重生成离线图标子集(单测会拦下过期的子集)
```

嫌两边分开开麻烦，根目录的 `dev-start.bat` 会在两个独立窗口里同时拉起后端 + 前端（起 Vite 之前先在 `web/` 下跑一遍 `npm install`），`dev-stop.bat` 停止它们。

::: warning 别手改 schema.d.ts
`web/packages/admin/src/api/schema.d.ts` 是从后端 OpenAPI 生成的契约文件，包里以 `KernelPaths` 导出。改了接口先跑 `npm run gen:api`（需要后端在跑），不要手写这个文件。
:::

## 包版本集中管理

后端依赖版本统一收在 [`backend/Directory.Packages.props`](https://github.com/SmartCode-X/SmartAdmin/blob/main/backend/Directory.Packages.props) 的 `<PackageVersion>` 里，新增或升级依赖改这里，**不要**在单个 `.csproj` 里单独锁版本。共享的构建/NuGet 元数据（作者、仓库地址、License 等）在 `backend/Directory.Build.props`。

## 提交信息：中文 Conventional Commits

代码、注释、文档和 git commit 统一中文，格式是 `type(scope): 主题`，其中 `type` 与 `scope` 保持小写英文，因为发版工具要按固定词表解析它们：

```text
fix(web): 无权限用户的按钮不再渲染
feat(backend): 新增定向通知投递
docs: 根目录配置与脚本文件的注释翻译
refactor(services): 登录流程拆成可覆写的虚方法步骤
```

常见 `type`：`feat` / `fix` / `docs` / `refactor` / `test` / `chore`。`scope` 一般是 `web` / `backend`，或更具体的模块名。完整规范见[提交规范](/zh/standard/commit)。

## 跑测试：两条腿都要绿

PR 上会跑 CI（后端四库矩阵、前端、模板冒烟），本地先跑通能少等一轮。改动 `backend/**` 之前，先跑 `dotnet build backend/SmartAdmin.slnx -c Release` 和 `dotnet test backend/SmartAdmin.slnx`。默认腿用 SQLite，`TestDb.cs` 按 `SMART_TEST_DBTYPE` 等环境变量给每个测试派生独立数据库，互不干扰。动了数据层就设 `SMART_TEST_DBTYPE=MySql` 加 `SMART_TEST_MYSQL` 连接串，再跑一遍 MySQL 腿。`RedisCacheTests` 的契约测试在没有 `SMART_TEST_REDIS` 时会静默跳过，改缓存时记得指向一个 Redis 再跑。

改 `web/**` 就跑 `npm run lint` → `npm run format:check` → `npm test`（vitest）→ `npm run build`（build 已包含 `vue-tsc` 类型检查，不用单独再跑 `typecheck`），和 CI 的前端那道检查一致。改了 `templates/**`，再跑一遍 `pwsh templates/smoke-test.ps1`：它把内核打进本地 feed，用 `dotnet new smart-app` 生成消费方项目再 build 并启动，也就是消费方拿到包后的第一条命令。

::: tip 可替换性契约测试不是普通测试
`ReplaceabilityTests` 锁定了 TryAdd 覆盖、虚方法重写、业务程序集挂载这几条可替换性保证。具体保证哪几条，在 [可替换性模型](/zh/backend/replaceability) 有完整清单。改动 DI 注册或 `SmartAdminSetup` 相关代码时，这组测试红了通常意味着破坏了消费方的替换路径，不要绕过或删测试，先看清楚破坏的是哪条保证。
:::

## PR 流程

1. 从 `dev` 切一个功能分支。
2. 改动尽量聚焦一件事，commit 信息按上面的规范来。
3. 本地跑通对应半边的 build/test/lint。
4. 提 PR，目标分支是 `dev`，CI 会替你跑一遍，描述里写清本地跑过哪些检查就行。
5. 如果你在用 Claude Code 或其它 AI agent 参与开发，仓库对 Issue 分诊、领域文档、业务开发 skills 有一套约定，见 [Agent Skills 与 AI 辅助开发](./agent-skills)。

## 安全问题

**不要通过公开 issue 报告安全漏洞。** SmartAdmin 以 NuGet 包和 npm 包分发，内置认证、RBAC 和多组织数据权限。公开报告等于在补丁出来之前就对所有下游消费方公布 0-day。

请走 GitHub 的私密漏洞报告：[新建 Security Advisory](https://github.com/SmartCode-X/SmartAdmin/security/advisories/new)，只有维护者能看到。维护者会在 7 天内响应，并与你协调修复和披露节奏。详见 [SECURITY.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/SECURITY.md)。

## 许可证

SmartAdmin 基于 [Apache License 2.0](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE) 开源，提交的代码默认以同一许可证贡献。
