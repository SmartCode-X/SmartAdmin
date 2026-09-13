# Commit Convention

::: tip In one line
Code, comments, docs and commit messages are all in Chinese; the format is conventional-commit — `type(scope): 主题` — with `type` and `scope` kept in lowercase English.
:::

## Why the subject is Chinese while `type` stays English

The two halves of a commit have different readers. The subject is written for people, and the people maintaining this repository are a Chinese-speaking team — their own language states the motivation for a change more precisely than English does. `type` and `scope` are written for machines: the tools that generate release notes and decide semantic versions parse them against a fixed vocabulary, which a Chinese word would break. Two languages, two jobs — not an oversight.

## Format

```
type(scope): 主题

[optional body: the motivation and impact, not a restatement of the diff]
```

- `type` — a fixed vocabulary, listed below.
- `scope` — optional; the area a change touches (module, package, or directory). Omit it for cross-cutting changes with no single clear scope.
- `主题` — one sentence saying what was done, with no trailing period.

## Vocabulary for `type`

Distilled from this repository's real history. Pick the closest match by meaning; don't invent new words:

| type | Used for |
|---|---|
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation only (README, design docs, comment translations) |
| `refactor` | Internal restructuring with no change in external behavior |
| `test` | Adding or adjusting tests |
| `build` | Build output and packaging metadata (a template package's icon or readme, say) |
| `ci` | CI/CD pipelines and the release process |
| `chore` | Maintenance: dependency bumps, ignore rules, regenerated artifacts — nothing that changes behavior |

## Examples

```
fix(web,backend): 每个操作按钮都挂上按钮级权限
fix(web): 无权限用户的按钮不再渲染
feat: 演示模式,展示部署下拦截所有写操作
feat(notice): 定向投递 + 通知铃富文本
feat(menu): 应用级权限路由选择器 + 菜单交互清理
refactor(web): 把重复的分页映射收敛成两个 helper
test: 补齐未覆盖服务区域的 CRUD 与端点用例
build(templates): 模板包补上图标与 readme
ci(release): 修发布流水线里的变量名笔误
docs: README 双语对齐 zh-CN 基线
```

Notes:

- `scope` can name one module (`web`, `notice`, `menu`) or several separated by commas (`web,backend`). When a change genuinely spans both sides, say so rather than forcing it into one.
- When there's no single clear scope — a repo-wide doc translation, a new cross-cutting capability — drop `scope` and write `type: 主题`.
- The subject may use `—`, `:` or parentheses for a short aside, but it stays one sentence. Don't turn it into several clauses.

## When to write a body

Only when one line of subject can't convey the **motivation** or the **blast radius**, for example:

- The fix addresses a subtle security problem whose trigger conditions and reach need spelling out.
- One change coordinates several places (a field renamed across frontend and backend, a config migration) and deserves a "why it was done this way" for whoever comes next.

A body is not a restatement of the diff — the diff already shows which lines changed. It supplies the context the diff can't. Everyday small fixes need no body; one subject line is enough for a style tweak or a single bug fix.

## Common mistakes

::: warning Don't
- End the subject with a period.
- Write `type` or `scope` in Chinese — that half is parsed by tooling and its vocabulary is fixed.
- Pack unrelated changes into one commit and paper over them with a vague `type` (a `feat` and a `fix` in the same commit, say). Split them by meaning.
- Use a `type` that never appears in this repository's history (`update`, `change`). `feat` / `fix` / `refactor` already cover the meaning; a new word only makes the history inconsistent.
:::
