# 组件生态

前端有两个和业务无关的通用组件来自**独立 npm 包**：不依赖 SmartAdmin，任何 Vue 3 + Naive UI 项目都能单装，本管理端用的也是同一套。

| 包 | 定位 | 文档 |
|---|---|---|
| [`smart-naive-table`](/zh/components/smart-table) | 列驱动数据表格 `SmartTable`：一个 `columns` 同时驱动搜索表单、字典渲染、列设置；一个 `fetcher` 适配任意后端 | [查看 →](/zh/components/smart-table) |
| [`smart-naive-icon`](/zh/components/smart-icon) | 离线优先的图标渲染器 `SmartIcon` 与选择器 `SmartIconPicker`，基于 Iconify：多图标库、注册过的图标集渲染不发请求、单字符串值 | [查看 →](/zh/components/smart-icon) |

> 两个包均已发布到 npm。要查某个 prop 叫什么名字、有哪些默认值，站上没有，去各自仓库的 README 找。这两页只回答两件事：要不要用它，怎么接进 SmartAdmin。

`smart-admin-web` 把这两个包声明为 peer 依赖，应用在自己的 `package.json` 里装一份，内核和应用共用它。这样表格的全局默认值、图标注册表都只有一份。在 SmartAdmin 里怎么接入、主题与图标怎么对齐，见 [主题与图标](/zh/frontend/appearance) 与上面两个包各自的文档页。
