# 变体四：详情页（Detail 模式）

按记录展示只读详情，不走弹窗。两种形态，**共用一个 `detail.vue` 组件**：
- **形态 b — 独立标签**：`router.push('/<模块路径>/<id>/detail')`，每条记录一个可关闭标签（标签以 `route.path` 为主键，天然一 id 一签）。
- **形态 a — 就地切换**：列表页里 `detailId` 状态 + `v-if`，把列表整块换成详情，同一标签内切换。

## 路由：约定式自动注册（不写 routes.ts）

约定：**页面表里任意 `detail.vue` = 一个 `/<其路径>/:id/detail` 详情路由**。页面表 = 内核内置页 + `createSmartAdmin({ views })` 传入的页（`web/packages/admin/src/router/viewRegistry.ts`），`web/packages/admin/src/router/detailRoutes.ts` 的 `registerDetailRoutes()` 从中挑出 key 以 `/detail` 结尾的注册；它在 `useAuthMenu.buildRoutesForModule` 里随菜单路由一起重建，故登录 / 切应用 / **F5 深链**都安全（动态路由只活内存，靠这次重建复活）。

- 加一个详情页：系统模块丢进 `web/packages/admin/src/views/<模块>/<页>/detail.vue`，业务模块丢进应用的 `src/views/<模块>/<页>/detail.vue`（落在 `main.ts` 那条 `import.meta.glob('./views/**/*.vue')` 里即可），**不写任何路由代码**。
  例：`views/system/user/detail.vue` → 自动得 `/system/user/:id/detail`，路由名 `detail-system-user`。
- 详情路由默认 `meta.noCache: true` → 排除出 keep-alive（只读页复访本就该拉新；也规避同名参数路由的缓存坑）。

## 组件：DetailPage + useTabTitle

业务模块 `import { DetailPage, useTabTitle, useTabsStore } from 'smart-admin-web'`；系统模块走包内路径 `#/components/DetailPage/index.vue`、`#/composables/useTabTitle`、`#/stores/tabs`。

- `DetailPage`（`components/DetailPage`）：返回 + 标题 + body/actions 插槽。`@back` 交父级 —— 路由态关标签回列表，就地态清 `detailId`。补偿非菜单路由的空面包屑（详情路由不在菜单树，头部面包屑为空）。
- `useTabTitle()`：数据加载后 `setTabTitle(记录名)`，把标签名从「详情」改成记录名（如「张三」）；内部置 `titleFixed`，`addTab` 复访不覆盖，随 tab 持久化、F5 无闪。**就地态别调**（那时 `route.path` 是列表标签，会改错标签）。

## detail.vue 骨架（路由 / 就地共用）

```vue
<script setup lang="ts">
const props = defineProps<{ id?: number | string }>()   // 传了 = 就地态；否则读 route.params.id
const emit = defineEmits<{ back: [] }>()
const isInline = computed(() => props.id != null)
const uid = computed(() => Number(props.id ?? route.params.id))
// watch(uid, load, { immediate: true })  —— 同名参数路由可能复用实例，用 watch 而非 onMounted
// load 成功后:if (!isInline.value) setTabTitle(record.name)
// onBack:isInline ? emit('back') : router.push(列表路径).then(() => tabs.removeTab(route.path))
</script>
<template>
  <DetailPage :title="title" :loading="loading" @back="onBack"> …n-descriptions… </DetailPage>
</template>
```

列表页入口（在你的列表页操作列，如「更多▾」下拉）：
```ts
{ key: 'detailTab' }    → router.push(`/system/user/${r.id}/detail`)  // 形态 b
{ key: 'detailInline' } → detailId.value = r.id                        // 形态 a
```
就地渲染与列表 `v-else` 二选一：`<UserDetail v-if="detailId != null" :id="detailId" @back="detailId = null" />`。行操作用 `authStore.hasPerm('GET:.../{id}')` 门控。

## 注意事项 / 坑

- **命名固定 `detail.vue`、参数固定 `:id`**；要自定义参数名 / 一页多详情，退回显式静态路由：系统模块写进 `web/packages/admin/src/router/routes.ts` 的 `layout` 子路由；业务模块在 `createSmartAdmin({ install })` 里调 `router.addRoute('layout', { path, name, component: namedPage(name, loader), meta })`（`router`、`namedPage` 从 `'smart-admin-web'` 导入）。
- **无路由级权限门**：守卫默认放行详情路由，管控靠行操作 `hasPerm` + 后端 API `[RolePermission]`；手敲 URL 仍命中被授权 API，取不到走错误态。
- **多应用且无默认 / 记忆应用**时，F5 深链详情会先落应用选择器（与菜单页深链同行为），进应用后即可达。
- 记录名含 `.` 时 `TabsBar` 会把它当 i18n key（缺键原样打印），既有小怪癖。

**基建源码：** `web/packages/admin/src/router/detailRoutes.ts`、`web/packages/admin/src/components/DetailPage/`、`web/packages/admin/src/composables/useTabTitle.ts`（内核未内置示例页；按上面骨架在你的模块下丢一个 `detail.vue` 即可）
