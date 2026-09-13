---
name: create-page-variant
description: 非标准前端页面模板——树表(Org 模式)、主从分栏(Dict 模式)、侧栏筛选(User 模式)、详情页(Detail 模式)、上下分栏定高 + 放大(Split 模式)五种变体及选型速查。当页面不是平铺列表+分页、或要求整屏定高只有表体滚时使用;平铺 CRUD 用 create-crud-frontend。
---

先读仓库根目录的 `skills/create-page-variant.md`(只有选型速查,很短)定下变体,再**只读**对应的那一份 `skills/create-page-variant/<变体>.md` 并严格按它执行:`tree-org` 树表 / `master-detail-dict` 主从分栏 / `sidebar-user` 侧栏筛选 / `detail` 详情页 / `split-zoom` 上下分栏定高 + 放大。这些文件是单一真源,本文件只是入口包装。平铺 CRUD 走 `skills/create-crud-frontend.md`;组件契约与页面形状(高度链类名)在 `web/COMPONENTS.md`。
