# SmartAdmin Development Skills

本目录包含 SmartAdmin 的开发规范文档（skills），帮助开发者（或 AI 助手）按照项目既定模式快速搭建模块，无需手动翻源码对着抄。

这不是代码生成器——每个 skill 是一份规则说明 + 参考模板，AI 助手读取后根据你的需求生成符合规范的代码。

## Skills 列表

| 文件 | 用途 | 适用场景 |
|---|---|---|
| [new-module.md](new-module.md) | **新增模块全流程编排** | 从零加一个完整模块时的入口,串起下面各专项 skill |
| [create-entity.md](create-entity.md) | 创建 SqlSugar 实体类 | 新建表、新建实体 |
| [create-crud-backend.md](create-crud-backend.md) | 创建后端 CRUD 全套 | Models + Interface + Service + ErrorCode + DI + Controller |
| [create-crud-frontend.md](create-crud-frontend.md) | 创建前端 CRUD 页面（Vue 3 + Naive UI） | Types + API + Vue 页面（SmartTable + FormContainer） |
| [replace-service.md](replace-service.md) | 替换/扩展内置服务 | 定制登录流程、换密码哈希、覆写服务步骤、覆盖内置前端页与文案 |
| [wire-import-export.md](wire-import-export.md) | **给自己的实体接导入导出** | 装 `SmartAdmin.Excel`、`IImportProfile`/`IExportProfile`、六个端点、菜单取号、两坑 |
| [create-job.md](create-job.md) | **给自己的模块加定时任务** | 写 `IAdminJob`、注册一行、后台建任务或写种子;HTTP/SQL 任务与五个常见坑 |
| [create-page-variant.md](create-page-variant.md) | 非标准页面模板 | 只做选型；五种变体各一份文件在 [create-page-variant/](create-page-variant/) 下：树表、主从分栏、侧栏筛选、详情页、上下分栏定高 + 放大 |
| [write-commit.md](write-commit.md) | **提交信息规范**（`/write-commit`） | 写 git commit 信息；中文主题 + conventional-commit 格式 |
| [write-docs.md](write-docs.md) | **文档写作规范** | 写或改 `site/` 下任何一页；中英双语的口吻、标点、开头、破折号 + 闸门 |
| [smart-release.md](smart-release.md) | **SmartAdmin 发版**（`/smart-release`） | 定版 → CHANGELOG → 前端包 + 文档站徽章版本号 → 验绿 → 合 main → 打 tag → 盯 release workflow / 改 Release 说明 |

## 使用方式

### Claude Code / Grok

项目已配置 `.claude/skills/` 包装（薄壳，单一真源仍是本目录的 md），直接输入：

```
/new-module
/create-entity
/create-crud-backend
/create-crud-frontend
/replace-service
/wire-import-export
/create-job
/create-page-variant
/write-commit
/write-docs
/smart-release
```

也支持自动触发——对 Claude 说"帮我加一个产品管理模块"即可匹配对应 skill。Grok 同样扫描 `.claude/skills/`。唯一的例外是 `/smart-release`：它合 `main`、打 tag、触发发包，所以标了 `disable-model-invocation`，只能由人键入，模型不会自己进入。

### Codex

Codex **不扫** `.claude/skills/`。项目级 skill 要放在：

| 路径 | 作用 |
|------|------|
| `.agents/skills/<name>/SKILL.md` | 跨 agent 项目级（推荐） |
| `.codex/skills/<name>/SKILL.md` | 仅 Codex 项目级 |
| `~/.codex/skills/<name>/SKILL.md` | 用户全局 |

这两处的包装由 `node scripts/gen-skill-wrappers.mjs` 从 `.claude/skills/` 镜像过去（`--check` 在 `ci.bat` 的 docs 闸门里跑），别手改；真源仍是本目录的 md。Claude Code 私有的 frontmatter 字段（`disable-model-invocation`、`argument-hint`）镜像时会剥掉。装完或新增 skill 后**重启 Codex 会话**才会进列表。

### 其他 AI 工具

在对话中引用对应文件：

> 参考 skills/create-entity.md，帮我创建一个 BizProduct 实体

### 全栈 CRUD 完整流程

新增一个完整模块，从 [new-module.md](new-module.md) 进入——它按顺序编排 实体 → 后端 → 测试 → `gen:api` → 前端 → i18n → 菜单/权限 → 验证，并列出步骤间最容易断的交接点（权限码四处一致、MsgKey 对 i18n 键、种子 Id 保留区间）。前端是 npm 包 `smart-admin-web`（Vue 3 + Naive UI，源码 `web/packages/admin/`）加应用壳 `web/template`。

建实体、建后端、建前端这三个 skill 连同 `new-module` 都会区分**系统模块**（内核维护者）和**业务模块**（消费者二开）两种模式；`replace-service` 只面向消费者，`create-page-variant` 只按页面形态分变体，都没有这条分叉。

### 消费者仓库怎么用这套 skills

`skills/` 是按内核仓语境写的（命令、路径、「系统模块」分支）。`dotnet new smart-app` 出来的、或自己装了内核包的消费者仓库照下面三步接入；内核升级后**整目录覆盖 `skills/`** 即可，不用逐文件合并。

1. **原样拷 `skills/` 整目录，不改内容**。改了就和上游漂移，下次覆盖会丢。用不上的三份留着也无妨，在你的规则里声明「不适用」即可：`smart-release.md` 只管本仓发版；`write-docs.md` 只管本仓 `site/`；`write-commit.md` 只在你也用中文 conventional-commit 时适用。
2. **项目专属规则只写一份**：`.agents/skills/<项目>-conventions/references/project-rules.md`。开头一张「通用流程写的 → 本仓库实际」替换表（构建 / 测试 / 运行命令、服务与控制器目录、错误码取号、菜单落库方式、前端目录），后面才是你自己的政策。它的优先级高于 `skills/*.md`；Claude Code 侧再放一个 `.claude/skills/<项目>-conventions/SKILL.md` 入口指向它，不复制规则。
3. **每个专项 skill 一对 wrapper**：`.claude/skills/<name>/SKILL.md`（Claude Code）与 `.agents/skills/<name>/SKILL.md`（Codex 等）内容逐字相同。wrapper 只做三件事：先读通用流程，再读 project-rules，声明冲突以 project-rules 为准。

   ```markdown
   ---
   name: create-entity
   description: （照抄本仓 .claude/skills/create-entity/SKILL.md 的 description）
   ---

   先读仓库根目录的 `skills/create-entity.md`（通用流程），再读
   `.agents/skills/<项目>-conventions/references/project-rules.md`（本仓库覆盖规则）；
   两者冲突时**以 project-rules.md 为准**，它的「命令与路径」一节直接替换通用流程里的
   构建 / 测试 / 生成命令。本仓库永远是「业务模块（消费者）」模式，通用流程里
   「系统模块」段落一律跳过。
   ```

消费者自己的页面模式或流程（本仓没有对应通用流程的）另起 skill，wrapper 里写明「仓库根 `skills/` 下没有对应流程，别去那里找、也别去那里新建」。
