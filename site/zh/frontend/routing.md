# 路由与动态菜单

你在菜单管理里加一条菜单、填好组件路径、保存，它就成了一个能点进去的页面——这中间没有一张你手写的路由表。前端的路由来自两个互不相干的源：构建期就定死的**静态壳**，和登录后按当前应用的菜单树在运行时重建出来的**动态路由**。

门户怎么决定进哪个应用、守卫怎么把这两侧缝起来，这页不讲，那是[多应用门户与守卫](/zh/frontend/portal-guards)的事。

```text
staticRoutes(router/routes.ts)          buildRoutesForModule(useAuthMenu.ts)
  ├─ /login                                拉 personalApi.menu(moduleId)
  ├─ /oauth/callback                       拍平菜单树
  ├─ /mfa/bind                             对每个 Menu 类型节点:
  ├─ /boot-error                             component 字符串 → 页面表(viewRegistry)里的 loader
  ├─ /module(应用选择器)                     router.addRoute('layout', { name: 'menu-{id}', ... })
  └─ /  → layout(default.vue)
        ├─ /personal → PersonalLayout(二级壳)
        │     └─ profile / password / security / sessions / bindings
        ├─ /personal/notice
        └─ /:pathMatch(.*)*(404)

构建期定死,部署间不变。                   登录 / 切应用 / 硬刷新时各重建一次。
```

静态那一侧在两次部署之间从不变化。动态那一侧完全由当前选中的应用返回的菜单树决定。所以不同用户、不同角色、不同应用，挂在 `layout` 下的那批路由都各不相同。

## 静态路由

`router/routes.ts` 定义一棵顶层树（节选，完整定义见源码）：

```ts
export const staticRoutes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: () => import('#/views/login/index.vue'), meta: { public: true } },
  { path: '/oauth/callback', name: 'oauth-callback', component: () => import('#/views/oauth/callback.vue'), meta: { public: true } },
  { path: '/mfa/bind', name: 'mfa-bind', component: () => import('#/views/mfa/index.vue'), meta: { public: true } },
  { path: '/boot-error', name: 'boot-error', component: () => import('#/views/error/BootError.vue') },
  { path: '/module', name: 'module', component: () => import('#/views/module/index.vue'), meta: { title: 'module.choose' } },
  {
    path: '/',
    name: 'layout',
    component: () => import('#/layouts/default.vue'),
    children: [
      {
        path: '/personal',
        name: 'personal',
        component: () => import('#/layouts/PersonalLayout.vue'),
        redirect: '/personal/profile',
        children: [
          // profile / password / security / sessions / bindings,每个都经 namedPage 包一层
        ],
      },
      { path: '/personal/notice', name: 'personal-notice', component: namedPage('personal-notice', () => import('#/views/personal/notice.vue')), meta: { title: 'menu.notice' } },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('#/views/error/404.vue')) },
    ],
  },
]
```

这里几处取舍都是刻意的：

- **`/` 不设静态 `redirect`。** `redirect` 在路由 resolve 阶段就求值，早于全局守卫。那时菜单树很可能还没建好，算出来的落点必然是错的。`/` 真正落到哪由 `router.beforeEach` 里的守卫决定（见[多应用门户与守卫](/zh/frontend/portal-guards)）。
- **404 挂在壳内，而非顶层。** 打错一个 URL 时侧边栏、标签栏、退出按钮照样在，不会把人甩到一个光秃秃的页面外面去。
- **`/oauth/callback`、`/mfa/bind` 带 `meta.public: true`，`/boot-error` 不带。** 前两个是登录前也要能到的场景（SSO 回调结果页、MFA 绑定/恢复）；`/boot-error` 是门户重建失败时的落点，用户此时通常已经登录，只是动态路由没能建起来，公开与否没有意义。
- **`/personal` 是一个二级壳。** `PersonalLayout` 下挂着 `profile`/`password`/`security`/`sessions`/`bindings` 五个子页，URL 都在 `/personal/*` 下；`/personal/notice` 不在这个壳里，是与它平级的独立静态路由。两者在后端都走 `[ActiveSession]`，任何登录用户都能读，不需要具体权限码，做成菜单反而得先播种、再给每个角色都授权一遍，纯属多余功课。入口分别在顶栏通知铃铛的「查看全部」和顶栏用户下拉。

应用要加布局壳之外的顶级路由，比如挂墙大屏、独立打印页，就交给 `createSmartAdmin({ routes })`。装配时它们经 `router.addRoute` 注册，和 `/login`、`/module` 同级，没标 `meta.public` 的照样过登录守卫。

## 动态路由：菜单树→真实路由

`layout` 下除了个人中心那几条静态路由和 404 兜底，其余全部来自 `useAuthMenu.ts` 的 `buildRoutesForModule`：

```ts
export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  const viewKeys = new Set(viewComponentPaths())
  resetRouter()
  for (const node of flatten(tree)) {
    const route = describeMenuRoute(node, viewKeys)
    if (!route) continue

    if (router.hasRoute(route.name)) router.removeRoute(route.name)
    if (route.kind === 'iframe') {
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/embed/iframe.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, iframeSrc: route.iframeSrc },
      })
      registerDynamic(route.name)
      continue
    }

    if (route.kind === 'missing') {
      console.warn('[menu] 缺少视图组件:', route.component)
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/error/MissingRoute.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, missingComponent: route.component },
      })
      registerDynamic(route.name)
      continue
    }

    const loader = getView(route.viewKey)!
    router.addRoute('layout', {
      path: route.path,
      name: route.name,
      component: namedPage(route.name, loader),
      meta: { title: route.title, icon: route.icon, keepAlive: true },
    })
    registerDynamic(route.name)
  }
  registerDetailRoutes()   // 约定式详情路由,见下文
  auth.routesReady = true
}
```

整条链路是这样。先拉当前应用的菜单树，走 `personalApi.menu(moduleId)`，拍平成一维数组。再对每个 `type` 为 `MenuType.Menu` 的节点，拿它的 `component` 字符串去页面表里查。目录 `Catalog` 没有页面，按钮 `Button` 不是路由，两者都跳过。

页面表在 `router/viewRegistry.ts`。内置页在包里登记，插件和应用的页面由 `createSmartAdmin({ views })` 并进来，同名时后来者覆盖先到者。约定很直接：菜单的 `component` 字段就是 `views/` 之后的相对路径，去掉 `.vue` 后缀。例如 `system/user/index` 对应内置页 `views/system/user/index.vue`，应用里放一个 `src/views/system/user/index.vue`，这个键就换成应用的页面。

**组件缺失时仍会保留原菜单路径。** `node.component` 在页面表里找不到对应的键时，`buildRoutesForModule` 会打一条 `console.warn`，并把这条路由落成 `MissingRoute`。用户点进去能直接看到缺失的组件路径，不会被一个没有上下文的 404 误导。`viewComponentPaths()` 列出页面表的全部键，菜单表单的「组件路径」下拉就从它取值。菜单管理员用下拉选择路径，通常不会走到这条诊断分支。

每条注册出来的路由都带 `name: 'menu-{id}'` 和 `meta.keepAlive: true`，通过 `router.addRoute('layout', ...)` 挂在 `layout` 下。每个这样加进去的路由名，都会经 `registerDynamic(name)` 登记一下，好在登出或切应用时精确地把它们移除。这段逻辑在 `router/index.ts` 的 `resetRouter` 里。

## 外链与内嵌页：没有新的菜单类型

`MenuType` 只有 `Catalog`/`Menu`/`Button` 三种。外链和 iframe 内嵌都没有新增枚举值，而是复用既有的 `Path`/`Component` 两个字段，靠 `isHttpUrl()` 区分意图。`isHttpUrl()` 判断的是字符串是不是 `http(s)://` 开头：

| 想要的效果 | 怎么配 | 运行时怎么处理 |
| --- | --- | --- |
| 外链菜单 | `Path` 填完整 URL，`Component` 留空 | `buildRoutesForModule` 直接跳过，不注册路由；点击这条菜单由侧边栏/搜索另行 `window.open` |
| 内嵌 iframe 菜单 | `Path` 填内部路径，`Component` 填完整 URL | 注册一个通用的 `views/embed/iframe.vue` 路由，`Component` 里的 URL 存进 `route.meta.iframeSrc` |

两者的判据都是在 `Path`/`Component` 里挑一个，看它「像不像 URL」，不需要后端多加一个字段、多跑一条迁移。

## 没有页面的接口：目录就是权限组

同样不新增菜单类型。只给移动端、PDA 或第三方系统调用的接口没有页面，它们的权限按钮直接挂在一个目录下，目录不填 `Path` 和 `Component`。这个目录就是权限组：`buildRoutesForModule` 跳过它（不是 `Menu`），侧栏因为它没有可见子项而不显示它，全局搜索也不收它。角色授权界面把挂在目录下的按钮渲染成该目录组里「接口权限（无页面）」一行，照常勾选。菜单管理页里目录行也有「配置权限」入口，可以直接在目录上添加第一颗按钮。

内核种子里的 `GET:/api/v1/ping` 就是这么挂的（`Id=301`，挂在「系统运维」目录下）。

`views/embed/iframe.vue` 只在 `setup` 里取一次 `route.meta.iframeSrc`，存进本地变量，不跟当前路由做响应式绑定。为什么不绑定？因为内嵌页会被 `keep-alive` 缓存复用。假如 `src` 响应式地跟着全局路由变，切到别的标签时，这个已缓存实例就会把 `src` 重算成 `undefined`，页面变空白，或者算成另一个内嵌页的地址，后台悄悄换成另一个站点。`setup` 只跑一次，这份快照天然不会被后续导航污染。

## 详情页的约定路由

`views/**/detail.vue` 是另一条约定，不用碰路由表就能生效。任何模块下丢一个 `detail.vue`，就会多出一条 `/<模块路径>/:id/detail` 的路由，应用自己 `src/views` 下的也一样。`registerDetailRoutes()`（`router/detailRoutes.ts`）从页面表里挑出以 `/detail` 结尾的键，和 `buildRoutesForModule` 里的菜单路由**在同一处一并注册**，就是上面代码块末尾那行。所以登录、切应用、F5 深链重建时，详情路由跟着菜单路由一起复活，不会出现「菜单能进，详情页刷新就 404」的情况。

详情页默认不进 `keep-alive`（`meta.noCache: true`），标题先顶一个通用的「详情」占位，数据加载完成后用 `useTabTitle()` 把当前标签标题换成具体记录名（比如「张三」「工单 #123」）。这个 setter 只应该在「详情页是当前独立标签」时调用；如果详情是在列表页内就地展开（同一个标签里切换），调用它反而会把列表标签的标题改错。参数名固定是 `:id`，需要多参数或非 `id` 命名时这条约定就不够用了，退回显式静态路由。

## 页面缓存与具名组件

`layouts/default.vue` 用这样的写法缓存页面：

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive` 的 `:include` 是按渲染出来的组件的 **`name`** 来匹配的。对于 `<script setup>` 单文件组件，Vue 会从文件名推断这个 `name`。而 `views/**` 下几十个同名的 `index.vue`，推断出来的名字互相冲突，也对不上路由自己的名字（`menu-{id}`）。`router/namedPage.ts` 就是补这个洞的：

```ts
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const hit = cache.get(name)
  if (hit?.loader === loader) return hit.comp

  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  const comp = defineComponent({ name, render: () => h('div', { class: 'page-view' }, h(inner)) })
  cache.set(name, { loader, comp })
  return comp
}
```

不管静态还是动态路由，每个页面组件都经过 `namedPage` 包一层，给它一个显式的 `name`，恰好等于路由名。这样 `:include="tabs.cachedNames"`（其实是一串 `TabItem.name`，也就是路由名）才真能匹配上它。这层包装按 `name` 缓存在一个 `Map` 里，只有底层的 **loader 引用**变了才会重建。而 `import.meta.glob` 给每个文件返回的是同一个稳定函数。所以改一个不相关的菜单、触发一次完整的 `buildRoutesForModule` 重建，那些 `component` 路径没变的路由照样复用同一个组件对象。也就是说，`keep-alive` 的缓存条目原封不动，不会被逼着重新挂载。它还把懒加载组件包进单独一层 `<div class="page-view">` 根节点，因为 `default.vue` 的 `<transition mode="out-in">` 要求子节点是单一元素根，而不少页面模板本身是「主体 + 若干并排弹窗」的多根结构。

`stores/tabs.ts` 在这基础上补了一道保险：它的 `cachedNames` getter 只保留 `router.hasRoute(n)` 为真的标签。菜单重建之后，某个旧标签对应的路由还没被重新注册，那一小段窗口期里 `keep-alive` 就不会去匹配一个还不存在的名字。`refreshTab(name)` 通过设置 `excludeName` 并递增 `reloadKey` 强制来一次真实的重挂载（绕开缓存），`default.vue` 监听 `reloadKey` 短暂地 `v-if` 卸载再恢复路由出口来实现这一点。

::: tip 进度条与浏览器标题
路由进度条用的是 Naive 的 LoadingBar：`beforeEach` 开始，`afterEach` 结束，加载出错时 `onError` 标红。浏览器标题不归守卫管，由 `App.vue` 里的 `usePageTitle()` 按当前页标题和站点名拼出来，切语言也跟着变。
:::

要从零把这套链路走一遍：建视图组件、种一条菜单、把组件路径填对，看[加一个前端页面](/zh/guide/frontend-page)。
