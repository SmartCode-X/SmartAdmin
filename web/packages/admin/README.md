# smart-admin-web

SmartAdmin 的前端内核：布局壳、动态菜单路由、登录与鉴权、`v-auth` 权限指令、全部内置系统页（用户、角色、菜单、组织、岗位、字典、配置中心、日志、个人中心……）和共享组件。后端是 NuGet 包 [`SmartAdmin`](https://www.nuget.org/packages/SmartAdmin)，两边**同号发布**，配套使用。

包是预编译的（ESM + `.d.ts` + 一份 CSS），内置页不随应用源码走：升级只改版本号。

## 起步

从前端模板起步最省事，它就是一个装好这个包的薄壳应用：

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

dev server 起在 `5173`，`/api`、`/openapi`、`/hub` 反代到后端的 `5100`（用环境变量 `SMART_API_TARGET` 改目标）。

已有的 Vite + Vue 3 应用直接装：

```bash
npm install --save-exact smart-admin-web@X.Y.Z   # X.Y.Z = 后端 SmartAdmin 包的版本号
npm install vue vue-router pinia vue-i18n naive-ui @vueuse/core @iconify/vue smart-naive-table smart-naive-icon
```

第二行的九个是 peerDependencies。它们持有全局单例：app 实例、路由、状态、SmartTable 的全局默认、图标注册表。应用和内核必须共用同一份，所以由应用来装；版本落在包声明的范围之外时，`npm install` 会直接报错。

## 用法

```ts
// src/main.ts
import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'

createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
}).mount('#app')
```

应用自己的东西都经 `createSmartAdmin` 交给内核：

| 选项 | 作用 |
|---|---|
| `views` | 页面表。键取 `views/` 之后去掉 `.vue` 的路径，比如 `sample/doc/index`，就是菜单管理里填的「组件路径」。与内置页同名即覆盖内置页；`<模块>/detail.vue` 自动成为 `/<模块>/:id/detail` 详情路由 |
| `locales` | 文案。`locales/ext/<locale>/<命名空间>.ts` 默认导出该命名空间的键，按命名空间深合并进内置文案 |
| `routes` | 布局壳之外的顶级静态路由，比如整屏看板、打印页 |
| `icons` | 本地 SVG：`import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })` |
| `iconSets` | 应用自己的 `ph` 图标子集，启动时同步注册。用包带的命令生成：`npx smart-admin-icons` 扫描 `src` 里的 `ph:*` 名字，写出 `src/assets/icons/ph-subset.json`；`--check` 给 CI 用 |
| `menuTitles` | 菜单 path → i18n key，存量库的菜单标题是中文时用 |
| `install(app)` | 需要 app 实例的注册：`registerHeaderTool`、`registerMenuBadge`、自己的 `app.use` |
| `plugins` | 与上面同形的插件对象。覆盖顺序：内核 < 插件（按数组顺序）< 应用 |
| `apiBase` / `version` / `dev` / `brand` / `table` | API 根地址、页脚版本号、开发态、品牌默认值（`sys.site.*` 配置有值时以配置为准）、SmartTable 全局默认 |

组件、composables、stores、API 原语都从包根导入：

```ts
import { FormContainer, useConfirm, translateError, unwrap, pageParams, toPage } from 'smart-admin-web'
import { SmartTable } from 'smart-naive-table'
```

应用自己的端点：对着自己的后端跑模板带的 `npm run gen:api` 生成 `src/api/schema.d.ts`，再用 `createApiClient<paths>()` 建客户端。超时、Bearer + CSRF、401 刷新重放这条中间件链与内核客户端是同一条。

## 版本

版本号与 NuGet 包 `SmartAdmin` 一致：主版本 = 内核所用的 .NET 主版本，次版本加功能，修订号修 bug。内置页照同一版后端的接口写，所以 `package.json` 里精确钉住版本、不带 `^`；升级时后端的 `SmartAdmin` 包与前端的 `smart-admin-web` 填同一个数字，改动见 [CHANGELOG](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)。

## 文档

- 文档站：<https://smartcode-x.github.io/SmartAdmin/zh/>
- 共享组件目录：[web/COMPONENTS.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md)
- 源码与 issue：<https://github.com/SmartCode-X/SmartAdmin>

## 许可证

[Apache-2.0](./LICENSE)
