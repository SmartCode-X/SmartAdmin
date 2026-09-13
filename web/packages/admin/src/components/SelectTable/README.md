# SelectTable

弹窗表格选择器。输入框触发 `NModal`，内部用 `NDataTable` 分页加载；支持单选/多选、搜索字段、关键字搜索和 `keywordSearch` 模式。

```vue
<SelectTable
  v-model:value="form.userId"
  v-model:selected="user"
  :columns="userColumns"
  :fetcher="userApi.page"
  :row-key="row => row.id"
  :label="row => row.name"
  :title="t('user.title')"
  keyword-search
/>
```

- `value` / `values`：选中行主键（单选/多选）。
- `selected` / `selecteds`：选中行对象，方便父组件直接回显名称。
- `fetcher`：SmartTable 兼容的 `{ items, total }` 分页函数。
- `keywordSearch` + `keywordSearchPlaceholder` / `keywordParam`：单个模糊查询框（默认参数名 `keyword`），后端入参有 `Keyword`（编号或名称的或匹配）时优先用它，别拆成编号、名称两个框；不开这个模式才用 `searchFields` 逐字段查。
- `paginated`（默认 `true`）/ `unpagedSize`（默认 `200`）：档案类数据量小（岗位、字典项…）时传 `:paginated="false"`，一次拉全、表体内滚动，比翻页找更快；超过 `unpagedSize` 的仍要靠查询框收窄。
- `triggerVisible`（默认 `true`）：`false` 时不渲染内置的输入框触发器，改由父组件持模板 ref 调 `open()` 编程式打开（组件已 `defineExpose({ open })`）。
