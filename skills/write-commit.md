# 提交信息规范（Write Commit）

写 git commit 信息前读这一份。**中文描述，conventional-commit 格式**：`type(scope): 主题`。

## 格式

```
type(scope): 主题

[可选正文,说明动机与影响,不复述 diff]
```

- `type` — 固定词表，见下。不自造新词。
- `scope` — 可选，标注改动影响的范围（模块名、包名、目录名）。跨领域或没有明确单一范围时省略。
- `主题` — 一句话说清做了什么，不加句号。中文写，`type` 与 `scope` 仍是英文小写。

## type 取值

| type | 用于 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修复缺陷 |
| `docs` | 仅文档改动（README、设计文档、注释翻译等） |
| `refactor` | 不改变外部行为的内部重构 |
| `test` | 新增或调整测试 |
| `build` | 构建产物、打包元数据相关 |
| `ci` | CI/CD 流水线、发版流程 |
| `chore` | 维护性杂务：依赖升级、忽略规则、重新生成产物，不影响功能与外部行为 |

## 例子

```
feat(notice): 定向投递 + 通知铃富文本
fix(web): 无权限用户的按钮不再渲染
fix(web,backend): 每个操作按钮都挂上按钮级权限
refactor(web): 把重复的分页映射收敛成两个 helper
test: 补齐未覆盖服务区域的 CRUD 与端点用例
build(templates): 模板包补图标与 readme
ci(release): 修 OIDC 用户名笔误(hu531035580 -> hu53135580)
docs: README 双语对齐 zh-CN 基线
chore: 删除 React 模板及其全部引用
```

`scope` 可以是单个模块（`web`、`notice`），也可以逗号并列（`web,backend`）。改动确实横跨两侧就如实标注，不强行归到一个。全局性改动（整仓文档翻译、新增横切能力）省略 `scope`。

## 正文什么时候写

一行主题说不清**动机**或**影响面**时才加：

- 修的是隐蔽的安全问题，要说明触发条件与影响范围。
- 一次改动牵涉多处协同（前后端字段改名、配置迁移），要给后来者一份「为什么这么改」。

正文补的是 diff 里看不出来的背景，不是复述改了哪几行。日常小修小补一行主题足够。

## 不要做的事

- 主题结尾加句号。
- 把多个不相关的改动塞进一个 commit，再用一个笼统的 `type` 概括。按语义拆开。
- `type` 用词表外的自造词（`update`、`change`）。语义已被 `feat`/`fix`/`refactor` 覆盖。
- 正文逐行复述 diff。

## 沉淀位

| | |
|---|---|
| 单一真相源 | `skills/write-commit.md`（本文），改规矩只改这里 |
| 薄包装 | `.claude/skills/write-commit/SKILL.md`（`/write-commit`） |
| 指路 | `CLAUDE.md` 一行 |
| 面向消费者的公开版 | `site/{zh/,}standard/commit.md` |
