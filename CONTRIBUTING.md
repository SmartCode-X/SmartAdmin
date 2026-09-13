# 参与贡献

先跑起来，再改代码。后端内核是 .NET 10 + SqlSugar，前端内核是 npm 包 `smart-admin-web`（Vue 3 + Naive UI，源码在 `web/packages/admin`，薄壳模板在 `web/template`），两侧都在这一个仓库里。

- 上手、架构、部署：<https://smartcode-x.github.io/SmartAdmin/zh/>
- 仓库怎么分层、哪些约定是硬的：根目录 `CLAUDE.md`
- 术语表与已定的取舍：`CONTEXT.md`、`docs/adr/`

## 本地四道检查

CI 是最终闸门，本地跑一遍只是省一次往返。改到哪一半就跑哪一半，别整套空跑。

```bash
# 后端（默认 SQLite；动了数据层再补跑一遍 MySQL 腿，环境变量见 CLAUDE.md）
dotnet build backend/SmartAdmin.slnx -c Release && dotnet test backend/SmartAdmin.slnx

# 前端（build 里含 vue-tsc）
cd web && npm run lint && npm run format:check && npm test && npm run build

# 文档站（selftest 必须排在全站扫前面：闸门自己坏掉会静默放行一切）
cd site && npm run lint:prose:selftest && npm run lint:prose && npx vitepress build

# 模板（pack 内核 → 本地 feed → dotnet new smart-app → build → test）
pwsh templates/smoke-test.ps1
```

改了 `skills/` 下的流程文件或 `.claude/skills/` 下的包装,跑一次 `node scripts/gen-skill-wrappers.mjs` 把 `.agents/` 与 `.codex/` 同步过去(`--check` 只比对不写,`ci.bat` 的 docs 闸门会跑它)。三份不同步的后果是:同一个 skill 在 Claude Code 里能用,在 Codex 里根本看不见。Claude Code 私有的 frontmatter 字段(`disable-model-invocation`、`argument-hint`)只写在 `.claude/` 侧,镜像时会剥掉。

改了 `site/` 下任何一页，先读 `skills/write-docs.md`：中文是母版，英文是译文，标点与开头有硬规矩，`lint:prose` 只拦得住其中一半。

## 提交信息

**中文主题 + conventional-commit 格式**：`type(scope): 主题`。`type` 和 `scope` 保持英文小写，主题用中文，结尾不加句号。

```
feat(notice): 定向投递 + 通知铃富文本
fix(web): 无权限用户的按钮不再渲染
ci(release): 第三方 action 钉到 commit SHA
```

`type` 取值固定为 `feat` / `fix` / `docs` / `refactor` / `test` / `build` / `ci` / `chore`，不自造新词。完整词表、正文什么时候写、常见误区，见 `skills/write-commit.md`。

## 分支与 PR

`main` 只承载已发布的版本，日常改动提到 `dev`。PR 描述说清动机和影响面，diff 里看得出来的就不必复述。issue 用仓库的模板开，新进来的都带 `needs-triage`，等维护者分诊。

## 报安全问题

不要开公开 issue。按 `SECURITY.md` 的方式私下联系。
