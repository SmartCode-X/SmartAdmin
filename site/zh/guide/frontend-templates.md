# 前端模板

前端内核是一个 npm 包 `smart-admin-web`，登录、布局、动态路由和全部系统管理页都在包里。模板是装它的一层薄壳：拉下来之后 `main.ts` 里只有一次 `createSmartAdmin(...)` 调用，其余目录留给你自己的页面和文案。

路由、门户守卫、请求层、权限、国际化、主题这些实现细节，归[前端深入文档](/zh/frontend/structure)。

## 技术栈

| 项 | 值 |
|---|---|
| 模板目录 | 仓库里的 `web/template` |
| 前端包 | `smart-admin-web`，与 NuGet 包同号 |
| 技术栈 | Vue 3 + Naive UI |
| 状态管理 | Pinia |
| 表格封装 | SmartTable（`smart-naive-table`） |
| dev 端口 | `5173` |

类型化客户端由后端的 `/openapi/v1.json` 生成，`npm run gen:api` 跑一遍就刷新。错误码、数据权限、动态菜单全部由后端契约定义，前端这一侧只管渲染。

## 拉一份当起点

想在仓库里直接跑一遍看看，去[快速开始](/zh/guide/getting-started)。想把它当成自己项目的前端，拉一份不带 git 历史的模板：

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

dev server 起在 `5173`，`/api`、`/openapi`、`/hub` 反代到后端 `:5100`，换目标用环境变量 `SMART_API_TARGET`。拉下来的文件全部归你：

| 文件 | 管什么 |
|---|---|
| `src/main.ts` | 调 `createSmartAdmin`，把自己的页面和文案交给内核 |
| `src/api/client.ts` | 本应用的类型化客户端。模板里是 `createApiClient<KernelPaths>()`，第一次 `gen:api` 之后换成自己的 `paths` |
| `src/views/` | 自己的页面 |
| `src/locales/ext/` | 自己的文案，按 `<locale>/<模块>.ts` 放 |
| `scripts/gen-api.mjs` | `npm run gen:api` 的实现，从跑着的后端生成 `src/api/schema.d.ts` |
| `vite.config.ts` | dev 代理与构建分包 |
| `public/` | favicon 与 Logo 这类静态文件 |

内核不在这些文件里。升级内核只改 `smart-admin-web` 的版本号，做法见[升级到新版本](/zh/guide/upgrade)。

## 往内核里接东西

`main.ts` 里的那次调用就是全部接入点：

```ts
import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'

createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
  version: __APP_VERSION__,
}).mount('#app')
```

页面 key 是 `views/` 之后去掉 `.vue` 的路径。`src/views/sample/doc/index.vue` 的 key 就是 `sample/doc/index`，菜单管理里的「组件路径」填的正是它；和内置页同名，就覆盖内置页。`<模块>/detail.vue` 按约定成为详情路由 `/<模块>/:id/detail`。其余选项按需加：

| 选项 | 用途 |
|---|---|
| `routes` | 布局壳之外的顶级静态路由，比如整屏看板、独立打印页 |
| `menuTitles` | 菜单 path 到 i18n 键的映射，库里的菜单标题是中文时用 |
| `icons` | 本地 SVG 图标：`import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })` |
| `install(app)` | 挂载前的注册动作，比如 `registerHeaderTool`、`registerMenuBadge`、自己的 `app.use` |
| `plugins` | 与上面同形的对象数组。覆盖顺序是内核、插件、应用，后注册的优先 |
| `brand` | `{ title, logo }`，品牌默认值：配置 `sys.site.title`、`sys.site.logo` 为空时用它们，站点信息到达前的首帧也用它们。配置有值时以配置为准 |
| `table` | SmartTable 全局默认的密度和每页条数选项 |

## 改内置页

内置页在包里，没法原地改，路子有两条。一条是整页复制到自己的 `views/`，用同一个 key 覆盖，这一页从此由你维护，升级不再替你更新它。另一条是把需求提成内核的配置项。Logo 走的就是后者：它读配置 `sys.site.logo`，在后台改一个配置项就行，不用碰任何页面。
