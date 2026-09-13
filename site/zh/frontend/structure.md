# 项目结构与启动

`app.use(pinia)` 一旦排到 `app.use(router)` 后面，路由守卫就读不到 store。`web/` 这一侧的规矩多半藏在这种顺序里，不在额外的约定层里。想知道什么在哪、什么时候跑，翻文件就能得到答案。

数据权限、可替换性这类设计取舍见[核心概念](/zh/guide/concepts)；API 调用、权限、状态、i18n 这类写页面的具体约定见[前端规范](/zh/standard/frontend)。

## 目录结构

前端分两层。内核是 npm 包 `smart-admin-web`，布局、路由、stores、内置页面和共享组件都在里面，与 NuGet 包同号发布。你的应用是从 `web/template` 拉出来的一层薄壳，只放自己的页面、文案、API 类型和启动配置。内核仓的 `web/` 两层都在：`packages/admin` 是包，`template` 是薄壳。

应用这一层，路径相对于应用的 `web/`：

| 路径 | 职责 |
|---|---|
| `src/main.ts` | 调 `createSmartAdmin()`，把自己的页面表、文案、`apiBase`、版本号交给内核，再挂载 |
| `src/api/client.ts` | 应用的类型化客户端 `createApiClient()`，中间件链与内核同一条。默认用内核端点类型 `KernelPaths`，有了自己的端点就跑 `gen:api` 生成 `src/api/schema.d.ts`，换成它的 `paths` |
| `src/views/` | 自己的页面，与内置页同名即覆盖内置页 |
| `src/locales/ext/` | 自己的文案，按命名空间深合并进内置词典 |
| `vite.config.ts` | dev 代理、`__APP_VERSION__`、手动分包 |

内核包这一层，路径相对于 `web/packages/admin/src/`：

| 目录 | 职责 |
|---|---|
| `api/` | `client.ts`（`createApiClient` 与中间件链）+ `index.ts`（内置端点按域分组，外加 `unwrap` 等原语）+ 生成的 `schema.d.ts` |
| `assets/` | 离线图标子集 `icons/ph-subset.json`、内核自带 SVG、第三方登录品牌图 |
| `components/` | 可复用组件（FormContainer、Dict* 系列等，详见 `web/COMPONENTS.md`） |
| `composables/` | 与 UI 库无关的 `use*` 逻辑 |
| `directives/` | 自定义指令：`auth.ts` 定义 `v-auth` |
| `layouts/` | 布局壳：顶栏、侧栏、标签页、设置抽屉 |
| `lib/` | 小型初始化工具：`runtime.ts` 存运行期配置，`icons.ts` 导出 `setupIcons()` |
| `locales/` | 内置词典与 `i18n` 实例（`index.ts`） |
| `router/` | 静态路由（`routes.ts`）、页面表（`viewRegistry.ts`）、守卫与动态路由登记（`index.ts`） |
| `stores/` | Pinia 状态（`app`、`auth`、`user`、`tabs`、`dict`） |
| `styles/` | 设计令牌（`tokens.css`）与全局样式（`index.css`、`table.css`、`layout.css`） |
| `theme/` | Naive UI 主题覆写（`naive-theme.ts`、`accents.ts`、`mix.ts`） |
| `types/` | 手写类型（`menu.ts`、`api.ts`） |
| `utils/` | 工具函数（`error.ts`、`chunkUpload.ts`、`tree.ts`、`ua.ts` 等） |
| `views/` | 内置页面，按模块组织 |

`src/` 根下还有三个文件。`createSmartAdmin.ts` 是装配入口。`index.ts` 是包的公开 API 面，应用只能 `import { … } from 'smart-admin-web'` 拿到它导出的东西。`App.vue` 是根组件。

## 启动流程

应用的 `main.ts` 只有一次调用：

```ts
import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'
import phSubset from './assets/icons/ph-subset.json'

createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  iconSets: [phSubset],
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
  version: __APP_VERSION__,
}).mount('#app')
```

装配顺序在包的 `createSmartAdmin.ts` 里（节选，完整定义见源码）：

```ts
export function createSmartAdmin(options: SmartAdminOptions = {}): SmartAdminApp {
  runtime.apiBase = trimTrailingSlashes(options.apiBase ?? '')
  // …version / dev / brand 同样写进 runtime

  const layers: SmartAdminPlugin[] = [...(options.plugins ?? []), options]
  const icons: Record<string, string> = {}
  const iconSets: IconifyJSON[] = []
  for (const layer of layers) {
    if (layer.views) registerViews(layer.views)
    if (layer.locales) registerLocales(layer.locales)
    if (layer.menuTitles) registerMenuTitles(layer.menuTitles)
    if (layer.icons) Object.assign(icons, layer.icons)
    if (layer.iconSets) iconSets.push(...layer.iconSets)
    for (const route of layer.routes ?? []) router.addRoute(route)
  }
  setupIcons(icons, iconSets) // 注册离线图标集(ph 子集同步入库)+ 本地 SVG,不预热整集

  window.addEventListener('unhandledrejection', e => {
    if (reloadOnChunkError(e.reason)) e.preventDefault()
  })
  window.addEventListener('vite:preloadError', e => {
    if (reloadOnce()) e.preventDefault()
  })

  const pinia = createPinia()
  pinia.use(piniaPluginPersistedstate)

  const app = createApp(AppRoot)
  app.use(pinia) // 必须在 router 之前:守卫用到 store
  app.use(router)
  app.use(i18n)
  app.directive('auth', vAuth)
  app.config.errorHandler = (err, _instance, info) => {
    if (reloadOnChunkError(err)) return
    console.error('[SmartAdmin] 未捕获异常', info, err)
  }

  app.provide(SMART_TABLE_DEFAULTS, createSmartTableDefaults({ /* density、pageSizes、labels */ }))

  for (const layer of layers) layer.install?.(app)

  return {
    app,
    router,
    pinia,
    i18n,
    mount: target => {
      app.mount(target)
      return app
    },
  }
}
```

1. **运行期配置**先写进 `lib/runtime.ts`。内核各处读它，不读 `import.meta.env`：包是预编译的，那些值在库构建时就定死了。
2. **逐层并入**：插件按数组顺序，应用自己排最后，同名时后来者覆盖先到者。随后 `setupIcons()` 注册离线图标集与本地 SVG。`ph` 只同步注册一份按实际用到的名字生成的子集，子集外的名字由 `AppIcon` 懒加载整套，不做整套预热。
3. **两个全局兜底**先于应用创建就挂上：`unhandledrejection`（游离的 Promise 拒绝）和 `vite:preloadError`（懒加载 chunk 拉取失败）。两者都先问 `lib/chunkReload.ts` 的 `reloadOnChunkError`/`reloadOnce`——发版后旧 chunk 404 时自动重载一次拿新 `index.html`，靠 `sessionStorage` 记一次，每个标签页只自动救一次，救过一次后交给内容区的 `ErrorBoundary` 让用户自己决定。
4. **Pinia**（装了 `pinia-plugin-persistedstate`），必须注册在 router 之前，因为路由守卫要读 store 状态。
5. 然后是 **router**、**i18n**。
6. 全局注册 **`v-auth` 指令**（`directives/auth.ts`），它按权限码控制元素显隐。紧接着设 `app.config.errorHandler`，兜住渲染期异常里 `ErrorBoundary` 够不着的那部分：命中 chunk 错误同样自动重载，否则打一行 `console.error`。
7. **SmartTable 默认配置**：给 `SMART_TABLE_DEFAULTS` 提供一份 `computed` 的 labels（搜索、重置、刷新、密度、列设置等），内部读 `i18n.global.t`。因为是 `computed` 且订阅了当前语言，切换语言时所有表格的文案会立即更新，各页面于是不用手动传 `:labels`。密度与每页条数可以经 `table` 选项改。
8. **各层的 `install(app)`** 在挂载前依次执行。`registerHeaderTool`、`registerMenuBadge`、自己的 `app.use` 这类注册写在这里。
9. **挂载**：`main.ts` 调返回值上的 `mount('#app')`。

包的 `index.ts` 引入了 `tokens.css`、`index.css`、`table.css`、`layout.css` 四份样式，库构建把它们抽成一份 `style.css`。应用在 `main.ts` 顶部 `import 'smart-admin-web/style.css'` 一次即可。

`App.vue` 是挂载目标，补上了装配函数没做的部分：

- 用 `n-config-provider` 包裹全部内容。`:theme` 和 `:theme-overrides` 来自 `useTheme()` 组合式函数。`:locale` 和 `:date-locale` 按 app store 的 locale 算出来，取的是 naive-ui 的 `zhCN`/`enUS` 与 `dateZhCN`/`dateEnUS`。
- 内部嵌套 `n-loading-bar-provider` > `n-message-provider` > `n-dialog-provider` > `router-view`，同层还挂着处理再认证的 `ReauthModal`。
- `onMounted` 时用 `useSite()` 的 `loadSite()` 拉一次站点品牌信息，这份信息是匿名的、全站共用的。浏览器标题和 `<html lang>` 交给 `usePageTitle()`，随路由和语言持续更新。
- 监听 app store 的 `locale`，把 `i18n.global.locale.value` 同步过去（`immediate: true`），这样应用任何地方切换语言都会立即反映到译文上。

## Dev 代理

应用的 `vite.config.ts` 做了代理，让浏览器只跟 `:5173` 通信：

```ts
const apiTarget = process.env.SMART_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
  },
},
```

后端 dev 环境默认端口是 5100。要让 dev server 指向另一个后端实例，启动 Vite 前设置 `SMART_API_TARGET` 即可。后端 CORS 默认 deny-all。本地开发能同源访问，全靠这层代理。

`vite.config.ts` 还会在构建期从 `package.json` 的 `version` 字段 `define` 出 `__APP_VERSION__`，`main.ts` 把它作为 `version` 交给内核，展示在登录页页脚。这个值在打包时就固化了，不走后端配置。

## 常用脚本

以下命令在应用的 `web/` 目录下执行：

| 脚本 | 命令 |
|---|---|
| `npm run dev` | `vite`：dev server,`:5173` |
| `npm run build` | `vue-tsc --noEmit && vite build` |
| `npm run preview` | `vite preview` |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run gen:api` | `node scripts/gen-api.mjs`（拉 `$SMART_API_TARGET/openapi/v1.json`，默认 `http://localhost:5100`） |

内核仓的 `web/` 是 workspace 根，脚本转发到两层。`npm run dev` 起模板，这时 `smart-admin-web` 指向 `packages/admin/src` 的源码，改内核页面有 HMR。`npm run build` 先构建包再构建模板。`npm test`、`gen:api`、`gen:icons` 只作用于包，`lint`、`format:check`、`typecheck` 两层都管。

::: warning gen:api 需要后端正在运行
`gen:api` 要从一个真实运行中的后端拉 `/openapi/v1.json`，所以要先启动后端（`dotnet run --project backend/samples/MinimalHost`，或直接跑 `dev-start.bat`）。生成的 `schema.d.ts` 禁止手改，因为下次跑 `gen:api` 就会被覆盖。
:::

仓库根目录还有两个批处理脚本，一次性管理整套服务：

| 脚本 | 作用 |
|---|---|
| `dev-start.bat` | 开两个窗口：后端（`dotnet run --project samples/MinimalHost`，`:5100`）和前端（`npm install && npm run dev`，`:5173`） |
| `dev-stop.bat` | 结束占用 `5100`、`5173` 端口的进程 |

这套结构跑通之后，往下一页是[路由](/zh/frontend/routing)：后端菜单树怎么拼成路由表。再往后是[请求流程](/zh/frontend/request)，一次接口请求怎么走过类型化客户端。
