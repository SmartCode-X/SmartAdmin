# ImportWizard

用户导入四步向导(也可注入其他档案的 api 复用):

1. **上传** — `n-upload` 拖拽 + 下载模板
2. **列映射** — 左=文件表头,右=`n-select` 目标列(自动匹配预选)
3. **预览改错** — **裸 `n-data-table`**(不用 SmartTable):可编辑单元格、错误格红底 + `n-tooltip` 显示 `translateError(code)`、「只看错误行」、「重新校验」、重复策略。**「库里已存在」(46010)不算硬错误** —— 后端按重复策略跳过/更新,只有「已存在记为错误」策略下才标红;其余策略走警示底色 + 「将按策略处理」,也不计进错误行数。判定见 `src/utils/importDup.ts`
4. **结果** — 计数;失败行可回到第③步;下载错误报告

演示模式下 preview/validate/commit 会被 `DemoModeFilter` 拦成 41002 —— 组件经 `translateError` 给出可读提示。

## Props / Model

| 属性                  | 类型                       | 说明                                                                   |
| --------------------- | -------------------------- | ---------------------------------------------------------------------- |
| `show`                | `boolean` (`v-model:show`) | 显隐;打开时重置状态                                                    |
| `api`                 | `ImportWizardApi`          | `downloadTemplate` / `preview` / `validate` / `commit` / `errorReport` |
| `title`               | `string?`                  | 弹窗标题;缺省 `import.wizardTitle`(「导入用户」)                       |
| `templateFileName`    | `string?`                  | 模板下载文件名,默认 `import-template.xlsx`                             |
| `errorReportFileName` | `string?`                  | 错误报告文件名,默认 `import-errors.xlsx`                               |
| `strategies`          | `DuplicateStrategy[]?`     | 第③步可选的重复策略,按给定顺序渲染;缺省三种都给                        |

`title` 的缺省值是内核用户导入的文案「导入用户」—— **复用到别的档案时务必传自己的标题**,
否则会顶着「导入用户」导别的东西。

`strategies` 用于**档案本身不支持某种策略**的场景 —— 例如库存(条码储位)导入只给 `[Skip, Error]`,
因为「覆盖已存在」等于把条码从原库位挪到新库位,那是移库业务、不该由一次导入代劳;
后端档案的 `CommitRowAsync` 同样要拦下 `overwrite`,不能只靠前端不给按钮来保证。

## Emits

| 事件   | 说明                                          |
| ------ | --------------------------------------------- |
| `done` | 提交有成功插入/更新时触发,父级 `refresh` 列表 |

## 用法(用户管理)

```vue
<ImportWizard
  v-model:show="importShow"
  :api="userImportApi"
  template-file-name="用户导入模板.xlsx"
  error-report-file-name="用户导入错误报告.xlsx"
  @done="() => tableRef?.refresh()"
/>
```

```ts
const userImportApi: ImportWizardApi = {
  downloadTemplate: () => userApi.importTemplate(),
  preview: (file, mapping) => userApi.importPreview(file, mapping),
  validate: rows => userApi.importValidate(rows),
  commit: (rows, strategy) => userApi.importCommit(rows, strategy),
  errorReport: rows => userApi.importErrorReport(rows),
}
```

权限:入口按钮 `v-auth="'POST:/api/v1/sys/user/import/preview'"`;模板下载走 `[ActiveSession]`,无需独立节点。
