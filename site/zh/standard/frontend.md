# 前端规范（Vue 3 + Naive UI）

写页面、调接口前对着这份清单核一遍。栈是 `<script setup>` + Naive UI + Pinia（持久化）+ vue-router + vue-i18n + VueUse。应用里路径别名 `@` → `src`，内核的组件、composables、stores、API 原语一律 `import { … } from 'smart-admin-web'`；内核包内部用 `#/` → `src`。整体架构翻 [核心概念](/zh/guide/concepts)；组件怎么用、设计系统长什么样，去仓库的 [`web/COMPONENTS.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md) 和 [`web/DESIGN.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/DESIGN.md) 找。

## 目录落点

- 页面按模块/实体分：`views/<模块>/<实体>/index.vue`，应用里就是 `src/views/`，由 `createSmartAdmin({ views })` 收进页面表；完整 CRUD 范例照内核包的 `views/system/menu/index.vue`（`SmartTable` + `FormContainer` 弹窗表单 + `useConfirm` 二次确认）。
- `composables/`（`use*`）放逻辑单源，默认与 UI 库解耦，错误与消息回调由视图注入。确需 Naive Provider 的交互样板是明确例外：`useConfirm` 内部直接用 `useDialog`/`useMessage`，`useTheme` 直接用 `darkTheme`，这类只能在 setup 里调用。
- 应用的 `api/`：`client.ts`（`createApiClient<paths>()`）+ 按域分的 `<域>.ts` + 生成的 `schema.d.ts`；内核包的 `api/` 是 `client.ts` + `index.ts`（内置端点按域分组）+ `schema.d.ts`。其余目录职责见 [项目结构](/zh/frontend/structure)。

## API 契约

::: warning schema.d.ts 是生成产物，禁手改
`schema.d.ts` 由后端 OpenAPI 生成，命令是 `npm run gen:api`。生成时**后端必须正在运行**，不然拉不到 `/openapi/v1.json`。手改它，下次一生成就被覆盖。要调类型，只能改后端接口或 DTO，再重新生成。生产环境为什么不挂这个端点，见 [常见问题](/zh/faq)。
:::

- API 调用集中在 `api/` 层按域分组（内置的在内核包 `api/index.ts`：`authApi`/`userApi`/`moduleApi`/`menuApi` …；你自己的模块新建 `src/api/<域>.ts`，`client` 用应用的 `./client`，`unwrap`/`pageParams`/`toPage` 从 `smart-admin-web` 导入），每个方法形如 `client.X(...).then(r => unwrap<T>(r))`，不在视图里裸调 `client`。
- `unwrap` 统一解信封，失败（`code≠0` 或非 2xx）都归一到 `ApiError`（带 `code`/`msgKey`）；视图 `catch` 后用 `translateError(e)` 出文案。
- 分页在 api 层归一为 `{ items, total }` 以适配 SmartTable 的 `fetcher`（后端是 `PagedList<T>{current,size,total,items}`）。
- 查询参数名用 PascalCase（ASP.NET 模型绑定要求）。
- 只有前后端真不同源（CDN / 独立域名）才构建期给 `VITE_API_BASE`，模板的 `main.ts` 把它作为 `apiBase` 交给内核，且后端要显式配 `SmartAdmin:Api:Cors:AllowedOrigins`（默认 deny-all）。鉴权与 401 刷新中间件在 [HTTP 请求层](/zh/frontend/request)，解信封细节在 [对接后端响应](/zh/frontend/api-contract)。

## 路由

- `router/routes.ts` 只放静态路由（login、error、shell/layout）；真实菜单树登录后从后端拉取，注入为动态路由（只活内存，不落盘）。
- 菜单节点的 `component` 串（如 `system/user/index`）是页面表的键，也就是 `views/` 之后去掉 `.vue` 的路径；应用 `src/views/` 下同名的页面覆盖内置页。路由 `name = menu-${id}`，挂在 `layout` 下。
- 登出 / 切应用用 `registerDynamic` / `resetRouter` 精确增删动态路由，不整体重置整棵路由树。
- 外链菜单（`path` 填 URL、`component` 留空）与内嵌 iframe 菜单（`component` 填 URL）复用现有字段，不新增菜单类型；`views/**/detail.vue` 是约定式详情路由（`/<模块>/:id/detail`），配 `DetailPage` 组件与 `useTabTitle()`。两条约定的机制见 [路由与动态菜单](/zh/frontend/routing)。

::: danger 不要持久化 routesReady / menuTree
持久化会跳过刷新重建流程，刷新后直接导向 404。这两个状态必须只活在内存里。重建机制见 [路由与动态菜单](/zh/frontend/routing)。
:::

## 状态（Pinia）

- `defineStore` + `actions`；持久化按需 `pick`，别把整个 store 无脑全量存：`auth` 只存 `currentModuleId`，`tabs` 只存 `tabs` 且落 sessionStorage。`user` 的令牌和用户信息少一个刷新就掉登录，走一份自定义 `serializer` 整份落 `localStorage`；Cookie 会话模式下令牌字段序列化时强制清空，只留 `cookieSession` 标记。
- 现有 store：`auth`（模块/菜单/权限码/`routesReady`）、`user`（令牌/登录态）、`app`（主题/偏好）、`tabs`（标签页）、`dict`（字典缓存，会话级内存、不持久化，增删改后 `invalidate` 失效）。登出走 `reset()` 清授权态并清标签。

## 组合式函数

- `use*` 命名，返回响应式引用与方法。
- 列表页统一用 `smart-naive-table` 的 `SmartTable` 远程模式：传 `:fetcher`，签名是 `(p: { page, pageSize, ...params }) => Promise<{ items, total }>`，分页与 loading 由 SmartTable 自己管。
- 已有 `useConfirm`（二次确认）、`useTabTitle`（详情页动态标签标题）、`useRealtime`（SignalR 实时推送客户端，鉴权外壳挂载时 `start`）等，用法见各自源码头注释与 `web/COMPONENTS.md`。

## 按钮级权限

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">新增</n-button>
```

- 单权限码传字符串；数组默认 OR,`.and` 修饰符做 AND；不命中靠 `display: none` 隐藏（响应式，权限刷新后自动跟着显隐），元素本身还在 DOM 里。
- 权限码取值就是后端的规范化路由（与 `[RolePermission]` 同源），不自造权限字符串。更细的取值规则在 [前端权限](/zh/frontend/permission)。

## 共享组件

- 后台不设组件演示菜单，组件用法统一沉在 [`web/COMPONENTS.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md)；写页面前先看一遍避免重复造轮子，加了新的通用组件也同步更新它。
- 已有 SmartTable / FormContainer / `useConfirm` / StatusSwitch / 字典组件（DictSelect、DictTag）/ OrgTreeSelect / FileUpload（`chunked` 走分片续传）/ ApiSelect（派生 UserSelect）/ UserPicker / PasswordStrength / Chart / CodeBlock / MarkdownEditor / DetailPage（详情页外壳，配 `useTabTitle`）/ IconPicker 等，完整清单以 `web/COMPONENTS.md` 为准，每个组件的详细 API 见内核包 `components/<组件>/README.md`。SmartTable 从 `smart-naive-table` 导入，其余从 `smart-admin-web` 导入。

## i18n

- 视图内所有可见文本走 `t('...')`，禁止硬编码中文 / 英文字面量。
- 错误文案不出后端：后端给 `code` + `msgKey`，`translateError` **优先按 `msgKey` 取字**，没有 `msgKey` 才退到内置的数字码白名单（`utils/error.ts` 的 `CODE_MSG_KEY`，只覆盖内核自己的码）兜底。所以 locale 里的键必须和后端 `[MsgKey]` 字符串逐字对上，比如后端标 `error.dict.typeNotFound`，前端词典里就要有同名的键；自定义错误码不在那份白名单里，写成 `error: { 60001: '...' }` 这样的数字键没人读，一样要走 `[MsgKey]`。内置文案在内核包的 `locales/zh-CN.ts`/`en-US.ts`；你自己的放应用的 `src/locales/ext/<locale>/<模块>.ts`，经 `createSmartAdmin({ locales })` 并入。机制见 [国际化](/zh/frontend/i18n)。

## 设计系统

- 业务代码只消费角色令牌层（如 `--color-text-primary`），不直接引原语层（如 `--color-gray-500`）；tokens 单源是内核包的 `styles/tokens.css`，随 `smart-admin-web/style.css` 发给应用。
- 组件样式用 `scoped` + CSS 变量（`var(--gap-card)` 等），不写死颜色 / 间距。
- 明暗切换靠 `<html data-theme="dark">`，不打即亮色；角色令牌 / 主色 / 语义色 / 阴影在其下整体翻转。完整规范在 [`web/DESIGN.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/DESIGN.md) 与 [主题与图标](/zh/frontend/appearance)。

## 提交前

```bash
npm run lint        # oxlint(lint:fix 自动修)
npm run typecheck   # vue-tsc --noEmit
npm run build       # vue-tsc --noEmit && vite build
```

三者都通过才算完成，不要只跑其中一个就认为没问题。模板拉出来的应用没配 lint，另两条照样要过。在内核仓的 `web/` 下改动，还要加跑 `npm run format:check` 和 `npm test`，CI 的前端检查这几样都跑。
