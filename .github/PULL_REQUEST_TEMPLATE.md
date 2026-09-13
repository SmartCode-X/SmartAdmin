## 改了什么

<!-- 一两句说清动机与影响面。diff 里看得出来的不必复述。 -->

关联 issue：

## 自检

- [ ] 提交信息是中文主题 + conventional-commit（`type(scope): 主题`，见 `skills/write-commit.md`）
- [ ] 本地跑过对应的那道检查（后端 / 前端 / 文档站 / 模板，命令见 `CONTRIBUTING.md`）
- [ ] 改了 `site/` 的话：读过 `skills/write-docs.md`，`lint:prose` 与 `vitepress build` 都绿，中英两侧事实对齐
- [ ] 新增或替换服务走的是 `TryAdd` + `virtual`，没有把消费方的扩展点堵上
- [ ] 目标分支是 `dev`（`main` 只承载已发布的版本）

## 破坏性变更

<!-- 动到 ErrorCode / AdminException / 实体基类字段 / sys_menu 列 / 权限码语义 / 配置节名的，在这里写清迁移动作；没有就写「无」。 -->

无
