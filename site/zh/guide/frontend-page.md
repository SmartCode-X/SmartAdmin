# 前端加一个页面

上一篇[端到端加一个业务模块](/zh/guide/business-module)已经把后端跑了起来。`GET/POST/PUT/DELETE /api/v1/sample/doc` 这组接口现在可调了。这一篇把它落成一个能点、能增删改的管理页面。

## 从 OpenAPI 契约重生成类型

前端不手写接口类型，而是从后端跑起来后暴露的 OpenAPI 契约生成。先让后端（带着刚加的 `sample/doc` 接口）跑着，再在 `web/` 下：

```bash
npm run gen:api
```

它执行的是 `node scripts/gen-api.mjs`（见 `web/package.json`）：抓后端的 `/openapi/v1.json` 重生成 `src/api/schema.d.ts`，新端点即出现在类型里。默认地址 `http://localhost:5100`，后端在别处就设 `SMART_API_TARGET`（和 dev 代理同一个变量）。**后端没在跑，这一步抓不到契约会直接失败**。

::: warning 别手改 `schema.d.ts`
它是生成产物，后端接口一变就重跑 `gen:api`，手改的内容下次生成整体覆盖。
:::

模板的 `src/api/client.ts` 起初用包里导出的 `KernelPaths`，只认得内核自己的端点。第一次生成出 `schema.d.ts` 之后，换成你自己的 `paths`，里面内核端点和你的端点都有：

```ts
// web/src/api/client.ts
import { createApiClient } from 'smart-admin-web'
import type { paths } from './schema'

export const client = createApiClient<paths>()
```

契约怎么从后端流到前端、`client.ts` 又是怎么据它做出带类型的请求，这些原理在[前端契约生成](/zh/frontend/api-contract)那篇讲过。这一篇只管拿来用。

## 加领域类型、封一层 API

新建 `web/src/types/sample.ts` 放领域类型（与后端 DTO 字段对齐，后端是驼峰序列化）：

```ts
/** 示例机构隔离文档(对齐后端 SampleDoc)。 */
export interface SampleDoc {
  id: number
  title: string
}
```

新建 `web/src/api/sample.ts`，按域封一组 API。请求走上一步的 `client`，统一信封用 `unwrap<T>` 解包。`unwrap`、`ApiError`，以及分页列表要用的 `pageParams`、`toPage`，都从 `smart-admin-web` 导入：

```ts
import { unwrap } from 'smart-admin-web'
import { client } from './client'
import type { SampleDoc } from '@/types/sample'

export const sampleDocApi = {
  list: () => client.GET('/api/v1/sample/doc', {}).then((r) => unwrap<SampleDoc[]>(r)),
  create: (title: string) =>
    client.POST('/api/v1/sample/doc', { body: { title } }).then((r) => unwrap<number>(r)),
  rename: (id: number, title: string) =>
    client.PUT('/api/v1/sample/doc/{id}', { params: { path: { id } }, body: { title } }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sample/doc/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```

`unwrap` 已经处理了两种失败形状：带 `code` 的业务信封，和不带 `code` 的 `ProblemDetails`。视图层直接 `catch` 后丢给 `translateError` 就行，不用在这里重复判断。信封解包与两种错误形状的细节见[请求与错误处理](/zh/frontend/request)。

`sample/doc` 的 `List` 接口不分页，直接返回数组。所以这里用不上 `toPage` 那套分页归一：把 `{page,pageSize}` 映射成后端的 `{Current,Size}`，把 `PagedList<T>` 映射成 `{items,total}`。那套是给真正分页的 `PagedList<T>` 端点用的，`pageParams` 和 `toPage` 跟 `unwrap` 一起从包里导入即可。写法参考内核 `web/packages/admin/src/api/index.ts` 里的 `userApi.page`、`dictAdminApi.typePage`，照着抄进你自己的模块就行。

## 写列表页

先看仓库里的 [`web/COMPONENTS.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md)。它是前端共享组件的索引，写页面前必读：页面用到的 `FormContainer`（弹窗/抽屉二合一表单容器）、`useConfirm`（二次确认 + 结果 toast）在这里都有约定和范例页指路。组件、composables、stores 都从 `smart-admin-web` 导入。

`sample/doc` 是不分页的平铺列表，不需要 `SmartTable`，用裸 `NDataTable` 就够。照内置字典页右侧字典项面板的写法来（`web/packages/admin/src/views/system/dict/index.vue`，那也是一张裸 `n-data-table` + 增删改）。新建 `web/src/views/sample/doc/index.vue`：

```vue
<script setup lang="ts">
import { h, reactive, ref, onMounted } from 'vue'
import { NButton, NCard, NSpace, NInput, NPopconfirm, NForm, NFormItem, NDataTable, useMessage, type DataTableColumns, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { FormContainer, translateError, useAuthStore, useConfirm } from 'smart-admin-web'
import { sampleDocApi } from '@/api/sample'
import type { SampleDoc } from '@/types/sample'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const rows = ref<SampleDoc[]>([])
const loading = ref(false)
async function load() {
  loading.value = true
  try {
    rows.value = await sampleDocApi.list()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

const columns: DataTableColumns<SampleDoc> = [
  { title: () => t('sampleDoc.title'), key: 'title' },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 130,
    render: (r) =>
      h(NSpace, { size: 4 }, () => [
        authStore.hasPerm('PUT:/api/v1/sample/doc/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sample/doc/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => sampleDocApi.remove(r.id), t('sampleDoc.deleted')).then((ok) => { if (ok) load() }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('sampleDoc.deleteConfirm', { title: r.title }),
              },
            )
          : null,
      ]),
  },
]

// ── 新增/编辑弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const form = reactive({ title: '' })
const rules: FormRules = {
  title: { required: true, whitespace: true, message: () => t('sampleDoc.titleRequired'), trigger: ['input', 'blur'] },
}
function openAdd() {
  editingId.value = null
  form.title = ''
  show.value = true
}
function openEdit(r: SampleDoc) {
  editingId.value = r.id
  form.title = r.title
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await sampleDocApi.create(form.title)
    else await sampleDocApi.rename(editingId.value, form.title)
    message.success(t('common.success'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <n-card :bordered="true">
    <template #header-extra>
      <n-button v-auth="'POST:/api/v1/sample/doc'" type="primary" size="small" @click="openAdd">
        {{ t('common.add') }}
      </n-button>
    </template>
    <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="(r: SampleDoc) => r.id" />
  </n-card>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('sampleDoc.addTitle') : t('sampleDoc.editTitle')"
    :width="420"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="60">
      <n-form-item :label="t('sampleDoc.title')" path="title">
        <n-input v-model:value="form.title" :placeholder="t('sampleDoc.title')" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
```

几处约定，都来自 `web/COMPONENTS.md`，不是这里现造的：

- **`FormContainer` 用 `onConfirm` 协议接管 loading/关闭**：`save()` 里把 `validate()` 放第一行，校验失败 reject 挡住关闭；接口失败 `return false`（或抛出）弹窗也不关，方便用户改后重试。业务代码不用自己管 `saving` 和底栏。
- **`useConfirm().run` 配 `n-popconfirm`**：popconfirm 当触发器，确认后的动作与成/败 toast 交给 `run`，`run` 回一个 `ok` 布尔，真删掉了再 `load()`。
- **按钮级权限，双重收口在同一份权限码上**：模板里的按钮用 `v-auth` 指令，值就是路由权限码 `POST:/api/v1/sample/doc`，不命中就靠 `display: none` 隐藏（响应式，权限刷新后自动跟着显隐）。操作列里的编辑/删除按钮走的是 `h()` 渲染函数，指令用不了，改用 `authStore.hasPerm(...)` 判断要不要渲染。两条路判定规则同一套：超管全放行，权限码没拉到时藏按钮，普通用户按码精确匹配。**这只是界面降噪，服务端始终是权威**。真正的拦截在后端 `[RolePermission]`，越权请求照样 403。规则细节见[前端权限模型](/zh/frontend/permission)。
- **错误处理留在视图层**：`catch (e) { message.error(translateError(e)) }`，不在 API 层弹 UI。`translateError` 优先按错误的 `msgKey` 到 locale 里取字，没有 `msgKey` 才退到内置的数字码白名单兜底。

## 挂进菜单，页面才可见

不用写路由。`main.ts` 里的 `views: import.meta.glob('./views/**/*.vue')` 已经把 `src/views/` 下的页面登记进内核的页面表，键是 `views/` 之后去掉 `.vue` 的路径。登录后内核按菜单树注册动态路由，拿菜单节点的 `Component` 字符串去页面表里找组件。每个菜单成为一条挂在 `layout` 下的路由，名字是 `menu-${id}`。所以你只要建好 `.vue`、再到**菜单管理**页建一个节点：

| 字段 | 值 | 说明 |
|---|---|---|
| Type | 菜单 | 目录只作父节点，按钮只承载权限码 |
| Path | `/sample/doc` | 路由地址 |
| Component | `sample/doc/index` | → `src/views/sample/doc/index.vue`（不带前后缀） |
| 所属应用 | 选一个应用 | 仅顶级目录有效 |

保存后重新登录（或刷新触发路由重建），菜单里就能看到这个页面了。要是控制台报 `[menu] 缺少视图组件`，就是 `Component` 字符串跟文件路径没对上。内核会保留原菜单路径并显示 `MissingRoute`，页面上能直接看到缺失的组件值。刷新或深链时，守卫怎么重建这些内存里的动态路由？见[动态路由与门户守卫](/zh/frontend/routing)。

## 补 i18n 文案

上面的页面用了一组 `sampleDoc.*` key（`common.*` 是全站共用的现成键，不用新加）。i18n 的键最终必须并进每个 locale 的同一个对象里，所以这里专门开了个扩展位：往 `web/src/locales/ext/<locale>/` 丢一个文件、默认导出你的键，`main.ts` 里的 `locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true })` 会把它并进内核的语言包。**文件名就是顶层命名空间**（`sampleDoc.ts` → `t('sampleDoc.*')`），你什么都不用注册：

```ts
// web/src/locales/ext/zh-CN/sampleDoc.ts
export default {
  title: '标题',
  titleRequired: '请输入标题',
  addTitle: '新增文档',
  editTitle: '编辑文档',
  deleteConfirm: '确认删除「{title}」?',
  deleted: '已删除',
}
```

```ts
// web/src/locales/ext/en-US/sampleDoc.ts
export default {
  title: 'Title',
  titleRequired: 'Please enter a title',
  addTitle: 'Add Document',
  editTitle: 'Edit Document',
  deleteConfirm: 'Confirm deleting "{title}"?',
  deleted: 'Deleted',
}
```

本例后端用返回值（`false`）表达失败，没有新错误码要翻。若你的模块往 `ErrorCode` 里加了码（像字典模块那样），把对应文案写成 `ext/<locale>/error.ts`。**键必须和后端 `[MsgKey]` 的字符串逐字对上**。`translateError` 优先按 `msgKey` 取字，没有 `msgKey` 时才退到内核内置的数字码白名单（`CODE_MSG_KEY`）兜底——但那份白名单**只收内核自己的码**，你自己模块的错误码不在其中。写成扁平的 `{ 50001: '...' }` 能编译、能解析，但永远没人读它，还是得走 `[MsgKey]`：

```ts
// 后端: [MsgKey("error.doc.titleDuplicated")] → 照抄成嵌套,去掉 `error.` 前缀
// web/src/locales/ext/zh-CN/error.ts
export default { doc: { titleDuplicated: '文档标题重复' } }
```

ext 是**深合并**：你的键是并进内置 `error` 命名空间，而不是把它整个顶掉。想改写某一条内置文案，比如 `{ auth: { passwordWrong: '...' } }`，也不会连坐同子树的兄弟键。错误码没标 `[MsgKey]` 时，后端发的是 `error.code.<数字>`。locale 里缺这条，就退回后端自己的 `message`，再退回 `error._fallback`。列标题写成 `title: () => t('...')` 的函数形式，切语言才即时生效。

## 提交前

```bash
npm run typecheck  # vue-tsc --noEmit
```

## 端到端自检

正文每一步都讲过了，这里只留一行核对项，配合上一篇的后端清单，一遍走完前端接线到能授权访问的全程。

**前端**
- [ ] `npm run gen:api` 重生成类型（后端在跑），`src/api/client.ts` 用的是自己的 `paths`
- [ ] 领域类型放 `types/<模块>.ts`，API 组放 `api/<域>.ts`，原语从 `smart-admin-web` 导入
- [ ] `views/<模块>/<实体>/index.vue`（表格 + `FormContainer` 表单 + 权限门控）
- [ ] i18n 双语文案放 `locales/ext/zh-CN/` + `ext/en-US/`（有新错误码就连翻译一起补）
- [ ] `npm run typecheck` 通过

**配置权限（运行时）**
- [ ] 菜单管理建节点（`Path` / `Component` 对上文件）
- [ ] 角色管理勾选授权，普通用户才看得见、点得动

页面跑通、自检过了，剩下就是把整套系统发到服务器上，看[部署概览](/zh/guide/deployment/)选一条上线路线。
