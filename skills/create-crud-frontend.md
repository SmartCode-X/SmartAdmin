# 创建前端 CRUD 页面 (Create Frontend CRUD)

为一个已有后端 API 创建完整的前端 CRUD 页面。前提：后端 CRUD 已完成（参考 `create-crud-backend.md`）。

> 前端是 Vue 3 + Naive UI。内核以 npm 包 `smart-admin-web` 交付（源码在本仓 `web/packages/admin/src/`）；消费方应用从 `web/template` degit 出来，只装这个包，工程里没有内核源码。

产出共 3 处文件改动。

## 第一步：确定模式

**这一步决定后面每个产出写进哪个工程、哪个文件。**

| 产出 | 系统模块（内核维护者，改 `web/packages/admin/src/`） | 业务模块（消费方应用，改应用自己的 `src/`） |
|---|---|---|
| Types | 追加进 `types/api.ts` | **新建** `src/types/<模块>.ts` |
| API | 追加进 `api/index.ts` | **新建** `src/api/<域>.ts`：`import { client } from './client'`，`unwrap`/`pageParams`/`toPage`/`ApiError` 从 `'smart-admin-web'` 导入 |
| i18n | 追加进 `locales/zh-CN.ts` + `en-US.ts` | **新建** `src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`（`main.ts` 的 `createSmartAdmin({ locales })` 已 glob 这个目录，深合并进内置文案，无需注册；见应用里的 `src/locales/ext/README.md`） |
| 页面 | `views/<模块>/<实体>/index.vue`（包内 `router/viewRegistry.ts` 自动登记） | `src/views/<模块>/<实体>/index.vue`（`main.ts` 的 `createSmartAdmin({ views: import.meta.glob('./views/**/*.vue') })` 登记） |
| 导入 | 包内路径 `#/…`（`#/` → `src/`；包里不写 `@/`，进了包 `@` 会解析到消费方的 `src`） | 内核的组件 / composable / store / 工具一律 `import { … } from 'smart-admin-web'`；应用自己的文件用 `@/…` |

两种模式下页面代码本身一样，只是导入来源不同。下面的模板按**系统模块**写，与内核真实页面 `web/packages/admin/src/views/system/position/index.vue` 同一写法；业务模块照上表换文件，导入块换成具名导入：

```typescript
import { SmartTable, type SmartTableColumn, type SmartTableInst } from 'smart-naive-table'
import { AppIcon, FormContainer, StatusSwitch, translateError, useAuthStore, useConfirm } from 'smart-admin-web'
import { productApi } from '@/api/product'
import type { BizProduct, ProductInput } from '@/types/product'
```

**系统模块**新增共享组件 / composable / 工具时，在 `web/packages/admin/src/index.ts` 具名导出并登记进 `web/COMPONENTS.md`：消费方只能导入那里导出的名字。

**业务模块的自检**：产出全在应用自己的 `src/` 下，`node_modules/smart-admin-web` 里的文件一个都不改（下次 `npm install` 就被覆盖）。要改内置页的行为，把需求做进内核当配置项；或把整页复制进自己的 `src/views/`、用同一个页面 key 覆盖，那一页从此不随内核升级，要自己维护。

## 前置步骤

确保后端已启动，然后重新生成 API 类型：

- **系统模块**：`cd web && npm run gen:api`，更新 `web/packages/admin/src/api/schema.d.ts`（包导出为 `KernelPaths`），内核的 `client` 随之获得新端点的类型。
- **业务模块**：在应用根目录 `npm run gen:api`，生成 `src/api/schema.d.ts`（内核端点 + 自己的端点）。第一次生成后把 `src/api/client.ts` 的 `createApiClient<KernelPaths>()` 换成 `createApiClient<paths>()`（`import type { paths } from './schema'`），应用的 `client` 才认得自己的端点。

---

## 产出 1：Types（类型定义）

文件：系统模块 → 追加进 `web/packages/admin/src/types/api.ts`；**业务模块 → 新建 `src/types/<模块>.ts`**（见「第一步：确定模式」）。

### 规则

- **行类型**（列表展示用）：`{Entity}` 接口，字段与后端实体对齐，`id: number` + 业务字段 + `createTime?: string`
- **输入类型**（新增/编辑用）：`{Entity}Input` 接口，只含业务字段（无 id、无 createTime）
- 后端 `long`（int64）在前端统一收敛为 `number`
- 可选字段用 `?` + `| null`

### 参考模板

```typescript
// 在 web/packages/admin/src/types/api.ts 末尾追加

/** 职位行(后端 SysPosition) */
export interface SysPosition {
  id: number
  name: string
  code: string
  sort: number
  enabled: boolean
  createTime?: string
}

/** 职位新增/编辑入参(后端 PositionInput) */
export interface PositionInput {
  name: string
  code: string
  sort: number
  enabled: boolean
}
```

---

## 产出 2：API 函数

文件：系统模块 → 追加进 `web/packages/admin/src/api/index.ts`；**业务模块 → 新建 `src/api/<域>.ts`**，顶部写 `import { client } from './client'` + `import { unwrap, pageParams, toPage } from 'smart-admin-web'`（见「第一步：确定模式」）。

### 规则

- 对象名 camelCase：`{module}Api`
- 四个标准方法：`page`, `add`, `update`, `remove`
- `page` 入参含 `{ page, pageSize, ...过滤字段, sortField?, sortOrder? }`
  - 用 `pageParams(params)` 映射为后端的 `{ Current, Size }`
  - 过滤字段手动映射为 PascalCase（后端 record 属性是 PascalCase）
  - 返回 `toPage<T>(r)` → `{ items: T[], total: number }`
- `add` / `update` / `remove` 用 `unwrap<T>(r)` 解包
- 路由字符串与后端 Controller 路由一致
- 别忘了在文件顶部的 `import type { ... }` 中追加新类型

### 参考模板

```typescript
// 在 web/packages/admin/src/api/index.ts 中追加

export const positionApi = {
  page: (params: { page: number; pageSize: number; name?: string; sortField?: string; sortOrder?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        params: {
          query: {
            ...pageParams(params),
            Name: params.name,
            SortField: params.sortField,
            SortOrder: params.sortOrder,
          },
        },
      })
      .then((r) => toPage<SysPosition>(r)),
  add: (body: PositionInput) =>
    client.POST('/api/v1/sys/position/add', { body }).then((r) => unwrap<number>(r)),
  update: (id: number, body: PositionInput) =>
    client.PUT('/api/v1/sys/position/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sys/position/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```

---

## 产出 3：Vue 页面

文件：系统模块 → `web/packages/admin/src/views/{module}/index.vue`（如 `web/packages/admin/src/views/system/position/index.vue`）；业务模块 → 应用的 `src/views/{module}/index.vue`。

### 规则

- 使用 `<script setup lang="ts">` + `<template>` 双段式
- **路由无需代码**——在后台「菜单管理」中新建菜单，`component` 字段填 `{path}/index`（如 `system/position/index`），动态路由自动注册

### 核心组件及用法

| 组件/工具 | 系统模块（包内路径） | 业务模块 | 用途 |
|---|---|---|---|
| `SmartTable` | `smart-naive-table` | `smart-naive-table` | 列定义驱动表格 + 搜索表单 + 分页 + 列设置 |
| `FormContainer` | `#/components/FormContainer/index.vue` | `smart-admin-web` | 新增/编辑弹窗（自动跟随全局 modal/drawer 设置） |
| `StatusSwitch` | `#/components/StatusSwitch/index.vue` | `smart-admin-web` | 行内启用/禁用切换（悲观更新，失败自动回滚） |
| `useConfirm` | `#/composables/useConfirm` | `smart-admin-web` | `run(fn, msg)` 执行+toast，`confirm(opts)` 确认+执行 |
| `useBatchDelete` | `#/composables/useBatchDelete` | `smart-admin-web` | 批量删除勾选 + 确认 + 执行 |
| `AppIcon` | `#/components/AppIcon.vue` | `smart-admin-web` | Iconify 图标 |
| `v-auth` | 全局指令，`createSmartAdmin` 已注册 | 同左，模板里直接写，不用 import | 按钮权限（值 = 路由权限码，如 `POST:/api/v1/sys/position/add`） |
| `useAuthStore` | `#/stores/auth` | `smart-admin-web` | `hasPerm(code)`：`h()` 渲染的行内按钮用它门控（渲染函数里挂不了指令） |
| `translateError` | `#/utils/error` | `smart-admin-web` | 错误对象 → i18n 文案 |

### 页面结构模式

```
<script setup>
  1. imports
  2. composables (useI18n, useMessage, useConfirm, useAuthStore)
  3. tableRef
  4. toInput 辅助函数(行数据 → Input 类型)
  5. columns 数组(驱动表格+搜索+列设置)
  6. 表单状态(show, formRef, editingId, rules, blank, form)
  7. openAdd / openEdit / save 函数
</script>

<template>
  <SmartTable :columns :fetcher :storage-key @error>
    <template #toolbar> 新增按钮(v-auth) </template>
  </SmartTable>

  <FormContainer v-model:show :title :on-confirm="save">
    <n-form :model :rules> 表单字段 </n-form>
  </FormContainer>
</template>
```

### 参考模板（完整可运行）

```vue
<script setup lang="ts">
import { h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NInput, NInputNumber, NPopconfirm,
  NForm, NFormItem, NSwitch,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { SmartTable, type SmartTableColumn, type SmartTableInst } from 'smart-naive-table'
import AppIcon from '#/components/AppIcon.vue'
import FormContainer from '#/components/FormContainer/index.vue'
import StatusSwitch from '#/components/StatusSwitch/index.vue'
import { useConfirm } from '#/composables/useConfirm'
import { positionApi } from '#/api'
import { useAuthStore } from '#/stores/auth'
import { translateError } from '#/utils/error'
import type { PositionInput, SysPosition } from '#/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<SmartTableInst<SysPosition>>()

// 行数据 → 入参(StatusSwitch 行内改状态 + openEdit 回填共用)
const toInput = (r: SysPosition): PositionInput => ({
  name: r.name, code: r.code, sort: r.sort, enabled: r.enabled,
})

const columns: SmartTableColumn<SysPosition>[] = [
  { type: 'index', title: () => t('common.rowNo'), width: 64, align: 'center' },
  { key: 'name', title: () => t('position.name'), search: true },
  { key: 'code', title: () => t('position.code') },
  { key: 'sort', title: () => t('position.sort'), width: 80 },
  {
    key: 'enabled',
    title: () => t('common.status'),
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        disabled: !authStore.hasPerm('PUT:/api/v1/sys/position/{id}'),
        request: (next: boolean) =>
          positionApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => { r.enabled = v },
      }),
  },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    fixed: 'right',
    hideInSetting: true,
    // h() 里挂不了 v-auth,行内按钮用 authStore.hasPerm 门控,码与工具栏 v-auth 同一格式
    render: (r) =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('PUT:/api/v1/sys/position/{id}')
          ? h(NButton, {
              size: 'small', quaternary: true, type: 'primary',
              onClick: () => openEdit(r),
            }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sys/position/{id}')
          ? h(NPopconfirm, {
              onPositiveClick: () =>
                run(() => positionApi.remove(r.id), t('position.deleted'))
                  .then((ok) => { if (ok) tableRef.value?.refresh() }),
            }, {
              trigger: () => h(NButton, {
                size: 'small', quaternary: true, type: 'error',
              }, () => t('common.delete')),
              default: () => t('position.deleteConfirm', { name: r.name }),
            })
          : null,
      ]),
  },
]

// ── 新增/编辑弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  name: {
    required: true, whitespace: true,
    message: () => t('position.nameRequired'),
    trigger: ['input', 'blur'],
  },
}
const blank = (): PositionInput => ({ name: '', code: '', sort: 0, enabled: true })
const form = reactive<PositionInput>(blank())

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
function openEdit(r: SysPosition) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await positionApi.add({ ...form })
    else await positionApi.update(editingId.value, { ...form })
    message.success(t('position.saved'))
    await tableRef.value?.refresh()
  } catch (e) {
    message.error(translateError(e))
    return false  // 返回 false 阻止 FormContainer 关闭
  }
}
</script>

<template>
  <SmartTable
    ref="tableRef"
    :default-page-size="100"
    :columns="columns"
    :fetcher="positionApi.page"
    flex-height
    virtual-scroll
    storage-key="sys-position"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/position/add'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>
        {{ t('common.add') }}
      </n-button>
    </template>
  </SmartTable>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('position.addTitle') : t('position.editTitle')"
    :width="480"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-form-item :label="t('position.name')" path="name">
        <n-input v-model:value="form.name" :placeholder="t('position.name')" />
      </n-form-item>
      <n-form-item :label="t('position.code')" path="code">
        <n-input v-model:value="form.code" :placeholder="t('position.codePlaceholder')"
          :disabled="editingId !== null" />
      </n-form-item>
      <n-form-item :label="t('position.sort')">
        <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="t('common.status')">
        <n-switch v-model:value="form.enabled" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
```

### 列表页默认形状（内核页面已统一，新页面照抄，不要逐页发明）

下面五条是内核 `views/system/**` 全部列表页的现状，也是消费者页面该长的样子。它们不在 SmartTable 的默认值里，漏写一条页面就会和站内其它页面不一样。

1. **操作列一个形状**：`key: 'op'`、`title: () => t('common.operation')`、`fixed: 'right'`、`hideInSetting: true`，`width` 按按钮数取（两个中文按钮 120，带英文文案或三个按钮 140–150）。渲染用 `h(NSpace, { size: 4, wrapItem: false }, () => [...])` 包一组 `h(NButton, { size: 'small', quaternary: true, type }, () => 文案)` 文字按钮，不带图标、不用实心 / `tiny` / `circle`；删除用 `NPopconfirm` 包同款按钮。语义色：编辑 `primary`、删除 `error`、审核 `success`、反审 `warning`、详情 / 复制 `info` 或 `default`。列多时横向滚动，操作列钉在右边才够得着；列少时 `fixed` 没有可见影响，但全站统一写上。固定列的实底是全局的（内核 `web/packages/admin/src/styles/table.css` 的 `.n-data-table-td--fixed-*`，随 `smart-admin-web/style.css` 全站生效），页面 scoped 里不要再写固定列底色或 `z-index`。
2. **每页条数**：每个 `<SmartTable>` 显式写 `:default-page-size="100"`；可选条数 `20 / 50 / 100 / 200` 由 `createSmartAdmin` 注入的 SmartTable 全局默认给出（`web/packages/admin/src/createSmartAdmin.ts`；上限 200 是后端 `Api:MaxPageSize` 的默认值；应用要换一组就传 `createSmartAdmin({ table: { pageSizes } })`），**页面里不要再写 `pageSizes`**。初始条数走 `default-page-size` prop，不在可注入的 defaults 里，漏写就以 10 起步，而 10 不在选项列表里，条数选择器会显示一个选不到的值。页面内自管分页的裸 `n-data-table` 要手动写同一组值。
3. **满屏与滚动**：SmartTable 加 `flex-height` + `virtual-scroll`，两者必须成对（没有 `flex-height` 就没有基准高度，单给 `virtual-scroll` 会把表体压塌）。页面形状按内核 `web/packages/admin/src/styles/layout.css` 的五种选一种套类名（模板顶层直接是 `<SmartTable>` 时什么都不用加），高度链只写在那一处，页面不抄 `:deep` 链；见 `web/COMPONENTS.md`「页面形状与高度链」。
4. **表高不要自己算**：不监听 `resize`，不写 `computed(() => window.innerHeight - N)`（`window.innerHeight` 不是响应式依赖，computed 永不重算，缩放窗口高度就僵住）。真要按视口算就用 `@vueuse/core` 的 `useWindowSize()`；绝大多数列表页压根不需要算，交给 `flex-height` + 容器 CSS。留白同理：外壳 padding 由布局的 `--pad-page` 给，页内块间距用 `gap: var(--gap-card)`，不写死数值，否则密度档切换时不跟着变。
5. **naive 组件写法**：模板里一律 kebab-case（`<n-button>` / `<n-form-item-gi>`），`<script>` 里 `h()` 引用才写 `NButton`；自有组件保持 PascalCase（`<SmartTable>` / `<FormContainer>`）。内核与应用模板都没有自动导入，用到的每个 `NXxx` 都要在同一个 SFC 里显式 `import`。弹窗表单要分节时用全局类 `.form-section` / `.form-section__title`（`styles/layout.css`），不在页面 scoped 里再画一遍标题。

### 关键模式速查

#### SmartTable columns 搜索

列加 `search: true` 即自动出现在搜索栏，参数名 = `key`：

```typescript
{ key: 'name', title: () => t('xxx.name'), search: true }
```

#### StatusSwitch（无独立启停端点时）

走全量 update：

```typescript
render: (r) => h(StatusSwitch, {
  value: r.enabled,
  request: (next: boolean) => api.update(r.id, { ...toInput(r), enabled: next }),
  'onUpdate:value': (v: boolean) => { r.enabled = v },
})
```

#### 删除确认

用 `NPopconfirm` + `useConfirm().run()`：

```typescript
h(NPopconfirm, {
  onPositiveClick: () =>
    run(() => api.remove(r.id), t('xxx.deleted'))
      .then((ok) => { if (ok) tableRef.value?.refresh() }),
}, {
  trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
  default: () => t('xxx.deleteConfirm', { name: r.name }),
})
```

#### FormContainer save 协议

- `on-confirm` 接收异步函数
- 返回 `false` 或抛异常 → 弹窗保持打开（用于校验失败/接口报错）
- 正常结束 → 弹窗自动关闭

#### 批量删除（useBatchDelete）

表格加勾选 + 工具栏批量删除按钮：

```typescript
// script setup 中(业务模块:import { useBatchDelete } from 'smart-admin-web')
import { useBatchDelete } from '#/composables/useBatchDelete'

const { checkedKeys, hasSelection, run: batchDelete } = useBatchDelete({
  remove: positionApi.batchRemove,  // 需要 API 中有 batchRemove 方法
  refresh: () => tableRef.value?.refresh(),
  successMsg: t('position.deleted'),
})
```

```vue
<!-- template 中 -->
<SmartTable
  v-model:checked-row-keys="checkedKeys"
  :row-key="(r) => r.id"
  ...
>
  <template #toolbar>
    <n-button type="error" :disabled="!hasSelection" @click="batchDelete">
      {{ t('common.batchDelete') }}
    </n-button>
  </template>
</SmartTable>
```

`useBatchDelete` 内部已封装：勾选态管理 + 二次确认对话框 + 成功后清选并刷新。

#### 字典字段

需要字典选择框的字段用 `DictSelect`/`DictTag`：

```typescript
// 业务模块:import { DictSelect, DictTag } from 'smart-admin-web'
import DictSelect from '#/components/DictSelect/index.vue'
import DictTag from '#/components/DictTag/index.vue'

// 列渲染
{ key: 'gender', render: (r) => h(DictTag, { code: 'gender', value: r.gender }) }

// 表单
<DictSelect v-model:value="form.gender" code="gender" />
```

---

## i18n（容易漏）

文件：系统模块 → `web/packages/admin/src/locales/zh-CN.ts`（及 `en-US.ts`）；**业务模块 → 新建 `src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`**，`export default { ... }` 直接写下面的键内容（文件名即顶层命名空间，`createSmartAdmin({ locales })` 深合并进内置文案，无需注册）。见「第一步：确定模式」。

新模块需要三处 key：

### 1. 模块自身（顶层 key）

```typescript
// 在 zh-CN.ts 的对应位置追加
position: {
  title: '岗位管理',
  name: '岗位名称',
  code: '岗位编码',
  sort: '排序',
  codePlaceholder: '不填则自动生成',
  nameRequired: '请输入岗位名称',
  addTitle: '新增岗位',
  editTitle: '编辑岗位',
  deleteConfirm: '确定删除岗位「{name}」?',
  saved: '保存成功',
  deleted: '删除成功',
},
```

### 2. 错误码翻译（`error` 对象下）

```typescript
error: {
  // ...已有的...
  position: { notFound: '职位不存在', codeExists: '职位编码已存在' },
},
```

MsgKey 与后端 `ErrorCode` 的 `[MsgKey("error.position.notFound")]` 严格对应。业务模块写进 `src/locales/ext/<locale>/error.ts`（`export default { <模块>: { … } }`），深合并进内置 `error` 命名空间，不会顶掉兄弟键。

### 3. 通用 key（已有，无需重复添加）

`common.rowNo`, `common.status`, `common.operation`, `common.add`, `common.edit`, `common.delete`, `common.save`, `common.createTime`, `common.batchDelete`, `common.batchDeleteConfirm` 等已在 `common` 下定义。

---

## 路由配置

**不需要写任何路由代码。** 在后台管理界面的「菜单管理」中：

1. 新建一个菜单节点
2. `component` 字段填页面 key：`views/` 之后、去掉 `.vue` 的相对路径（如 `system/position/index`）。系统模块对应 `web/packages/admin/src/views/` 下的文件，业务模块对应应用 `src/views/` 下的文件；与内置页同名的 key 覆盖内置页
3. 动态路由会自动注册该页面；表单的「组件路径」下拉列出全部已登记页面

**系统模块**还需要在后端 `DefaultMenuSeed.cs` 中添加菜单种子数据（含权限按钮），否则首次启动页面不会出现。详见 `create-crud-backend.md` 的「容易忽略的点 → 菜单种子数据」。

---

## 容易忽略的点

### 1. `api/index.ts` 顶部 import 行（**仅系统模块**）

追加 API 对象后，别忘了在文件开头的 `import type { ... }` 中补上新类型：

```typescript
import type { ..., SysPosition, PositionInput } from '#/types/api'
```

这行很长，容易漏加，漏了 TypeScript 会报错但错误信息指向 API 函数而非 import。

**业务模块不适用**——你的 API 在自己的 `src/api/<域>.ts` 里，类型从自己的 `@/types/<模块>` 导入，应用里没有 `api/index.ts`。

### 2. `v-auth` 权限码格式

值是 **`METHOD:/路由模板`**，与后端 Controller 路由 + 菜单种子 Permission 完全一致：

```vue
<n-button v-auth="'POST:/api/v1/sys/position/add'" ...>
<n-button v-auth="'PUT:/api/v1/sys/position/{id}'" ...>
<n-button v-auth="'DELETE:/api/v1/sys/position/{id}'" ...>
```

路径参数用 `{id}` 占位（与路由模板一致），不是具体数字。

`v-auth` 只能写在模板里。操作列、状态列这类 `h()` 渲染出来的按钮挂不了指令，改用 `authStore.hasPerm('PUT:/api/v1/sys/position/{id}')` 判断：没权限就不渲染（返回 `null`），开关类控件传 `disabled`。码的格式与 `v-auth` 相同。

### 3. `storage-key` 唯一性

`SmartTable` 的 `storage-key` 用于持久化列设置/密度偏好到 localStorage。每个页面的 key 必须全局唯一，建议命名 `{模块}-{实体}`（如 `sys-position`）。

### 4. 新增/编辑共用表单时的字段区分

某些字段在新增时可编辑、编辑时只读（如 `code`）。用 `:disabled="editingId !== null"` 控制。如果新增和编辑的字段差异较大，考虑用两个 `FormContainer` 而非一个。

---

## 检查清单

系统模块（本仓 `web/` 下）：

```bash
cd web
npm run typecheck   # 类型检查通过(包 + 应用模板)
npm run lint        # 代码规范检查通过
npm run format:check # 格式检查通过(prettier,CI 同一道)
npm test            # 单测通过(含 locales/parity.spec.ts 的中英键对齐)
npm run dev         # 启动开发服务器(模板直连内核源码),浏览器访问确认页面正常
```

业务模块（应用根目录）：

```bash
npm run typecheck   # 类型检查通过
npm run dev         # 启动开发服务器，浏览器访问确认页面正常
```

手动验证：
- [ ] 操作列 `fixed: 'right'` + `hideInSetting`，`:default-page-size="100"` 已写，`flex-height` + `virtual-scroll` 成对（见「列表页默认形状」）
- [ ] 表格加载、搜索、分页正常
- [ ] 新增保存成功、表格刷新
- [ ] 编辑回填正确、保存成功
- [ ] 删除确认弹窗 → 删除成功 → 表格刷新
- [ ] StatusSwitch 切换后刷新页面状态不回弹
- [ ] 无权限的按钮被隐藏（工具栏 `v-auth`，行内 `authStore.hasPerm`）
- [ ] 错误提示显示中文（i18n key 正确）
