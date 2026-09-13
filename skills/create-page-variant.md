# 非标准页面模板 (Page Variants)

`create-crud-frontend.md` 覆盖了最常见的 flat SmartTable CRUD（Position 模式）。本文只做选型：五种变体（树表、主从分栏、侧栏筛选、详情页、上下分栏定高 + 放大）模式差异较大，不能硬套 flat CRUD，各自一份文件在 `create-page-variant/` 下，**选定后只读那一份**。

> 组件契约见 `web/COMPONENTS.md`。代码片段按系统模块写（包内 `#/…`）；业务模块把内核的组件 / composable / 工具改成从 `'smart-admin-web'` 具名导入，落点见 `create-crud-frontend.md`「第一步：确定模式」。页面外壳的高度链（`.fill-page` / `.side-page` / `.fill-split` 等）全部定义在内核 `web/packages/admin/src/styles/layout.css`（随 `smart-admin-web/style.css` 全站生效），页面只套类名，不各自抄 `:deep` 链；`create-crud-frontend.md`「列表页默认形状」里的五条（操作列、`:default-page-size`、`flex-height` + `virtual-scroll`、不自己算表高、naive 组件写法）对每个变体同样成立。

## 选型速查

| 你的页面特征 | 选哪个 | 读哪份 |
|---|---|---|
| 平铺列表 + 分页 | flat CRUD | `create-crud-frontend.md` |
| 有父子层级 / 树结构 | 变体一：树表（Org 模式） | [create-page-variant/tree-org.md](create-page-variant/tree-org.md) |
| 一对多从属关系，需同时看两张表 | 变体二：主从 / 左右分栏（Dict 模式） | [create-page-variant/master-detail-dict.md](create-page-variant/master-detail-dict.md) |
| 列表需要额外维度筛选（树 / 分类 / 标签） | 变体三：侧栏筛选（User 模式） | [create-page-variant/sidebar-user.md](create-page-variant/sidebar-user.md) |
| 单条记录的只读详情（独立标签 / 就地切换，不走弹窗） | 变体四：详情页（Detail 模式） | [create-page-variant/detail.md](create-page-variant/detail.md) |
| 上下两块表、整屏定高只有表体滚、每块可放大 | 变体五：上下分栏定高 + 放大（Split 模式） | [create-page-variant/split-zoom.md](create-page-variant/split-zoom.md) |
| 只是「整页吃满一屏」，没有联动明细 | 不用变体：外壳加 `.fill-page`，一行类名就够 | — |
| 以上都不是 | 先看最接近的变体，按需调整 | — |
