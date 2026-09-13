# SmartIcon 与 SmartIconPicker

> `smart-naive-icon`：面向 Vue 3 + Naive UI 的离线优先图标包，基于 [Iconify](https://iconify.design)，渲染器是 `SmartIcon`，选择器是 `SmartIconPicker`。它是独立 npm 包，`smart-admin-web` 把它声明为 peer 依赖（`^2.0.0`），内核包里只留三层薄封装来接它。

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/smart-naive-icon"><img src="https://img.shields.io/npm/v/smart-naive-icon?color=cb3837&logo=npm" alt="npm"></a>
</div>

菜单表的 `icon` 字段里只存一个字符串，侧栏、面包屑、按钮照着它把对应图标画出来。挑图标、存图标、渲染图标这三件事，SmartAdmin 都交给这个包。它渲染时只读打进应用的本地数据，从不向 Iconify 的在线 API 发请求。

图标在 SmartAdmin 应用里怎么统一注册、`AppIcon` 怎么在全站渲染，归 [主题与图标](/zh/frontend/appearance) 那页。这里不重复讲接入约定。

## 在菜单管理里选一个图标

内核包里只有三个文件碰这个包，都在 `web/packages/admin/src` 下：

- `lib/icons.ts` 的 `setupIcons()` 由 `createSmartAdmin()` 调一次，把离线图标集和本地 SVG 全局注册好；
- `components/IconPicker/index.vue` 包着 `SmartIconPicker`，是应用里的选择器，用在**系统管理 → 菜单管理**的菜单 `icon` 字段上；
- `components/AppIcon.vue` 包着 `SmartIcon`，是应用里的渲染器，全站画图标都走它。

后两个以 `IconPicker`、`AppIcon` 的名字从 `smart-admin-web` 导出，应用的页面照样能用。菜单管理页把这两头串起来，代码在 `views/system/menu/index.vue`。表单里用选择器挑，表格列里用渲染器显示。

```vue
<!-- 表单:选一个图标存进 form.icon -->
<IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />

<!-- 表格列:把存下来的字符串画出来 -->
<AppIcon :icon="row.icon" :size="18" />
```

`v-model` 在这里写成 `model-value`，它的值就是**一个字符串**，形如 `ph:house-duotone`，也就是 `前缀:图标名`。本地 SVG 则是 `local:图标名`。这个字符串原样进数据库的 `icon` 字段，读出来交给 `AppIcon` 就能渲染。整个契约只有这一条：一个字段存一个字符串，选择器和渲染器两头都认它。

`setupIcons()` 已经全局注册过一遍，所以 SmartAdmin 的选择器封装没有再传 `collections`，直接复用。它只做一件包本身不管的事：把 vue-i18n 的文案算成 `labels` 注进去，详见下文。

## 离线优先意味着什么

「离线优先」不是说组件能离线跑，而是说**你注册进来的图标集，渲染时只读打进包里的本地数据，永远不碰 `api.iconify.design`**。每一套图标在你的构建里都是一个独立的懒加载 chunk，也就是 `@iconify-json/<prefix>`。第一次点开它的 Tab、或第一次渲染这套里的图标时，才拉进来。

代价是体积。每注册一套就多一个 chunk，大集不便宜：Phosphor 约 946 KB gz、Lucide 约 85 KB gz。所以按需注册，别一口气全塞进去。换来的是：部署环境不联网、或出口被限时，图标表现和联网时完全一致。包还留了一条在线兜底：你手输一个没注册过的 Iconify 名字，联网时会临时在线加载。那是应急，不是常态。

## 注册更多图标集

`setupSmartIcon` 是包的唯一配置入口，一次配好所有集。SmartAdmin 的 `setupIcons()` 就是它的一层封装，在内核包的 `lib/icons.ts`：

```ts
import { setupSmartIcon } from 'smart-naive-icon'

setupSmartIcon({
  collections: [
    { prefix: 'ph', name: 'Phosphor', loader: () => import('@iconify-json/ph/icons.json').then((m) => m.default) },
    { prefix: 'lucide', name: 'Lucide', loader: () => import('@iconify-json/lucide/icons.json').then((m) => m.default) },
    // 每多一套:先 npm i @iconify-json/<prefix>,再加一行同 prefix 的 loader
  ],
  preloadPrefix: '__none__', // 包没有「不预热」的开关:传一个必不存在的前缀,让预热落空
})
```

每个 collection 在选择器里是一个 Tab。`collections` 里的第一个就是默认打开的那个，SmartAdmin 里是 `ph`。要哪些集，到 [icon-sets.iconify.design](https://icon-sets.iconify.design) 挑，记下 `prefix`，装对应的 `@iconify-json/<prefix>`。存下来的值带前缀，比如 `ant-design:home-outlined`，所以不管当初在哪个 Tab 选的，`AppIcon` 都能对上号。

SmartAdmin 没有用 `preloadPrefix` 去预热某一整套集（`ph` 全集有 946 KB gz，首屏拉不起）。它另开一条路：启动时同步注册 `ph` 的子集，子集外的名字才由 `AppIcon` 懒加载整套 `ph`。子集有两份，内核的随包发布，应用的用 `smart-admin-icons` 从自己的 `src` 生成，见[主题与图标](/zh/frontend/appearance)。

如果你**根本不调** `setupSmartIcon`，包自带一套 Lucide 作兜底，导入即用。它把 `@iconify-json/lucide` 列为运行时依赖。SmartAdmin 没走这条默认路，而是显式注册了 ph / lucide / ep / ant-design 四套。这份清单在内核包里，应用要补的图标走下一节的本地 SVG。

## 塞进你自己的 SVG

设计给的图标不在任何 Iconify 集里，就注册成本地 SVG。Vite 下用 glob 把一个目录读成原始字符串，文件名去掉 `.svg` 就是图标名：

```ts
import { registerLocalIcons } from 'smart-naive-icon'

registerLocalIcons(
  import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
)
// src/assets/svg/star.svg  ->  存成 local:star
```

SmartAdmin 把这一步并进了 `setupIcons()` 的 `localIcons` 选项。应用把同样的 glob 交给 `createSmartAdmin({ icons })`，它和内核自带的 SVG 一起注册：

```ts
createSmartAdmin({
  icons: import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
})
```

往应用的 `src/assets/svg/` 丢 SVG，重启 dev 就能在选择器的「本地」页看到它。

## 文案与多语言

包里**不带任何 i18n 框架**。`SmartIconPicker` 上所有可见文字都来自一个 `labels` 对象，默认英文，你覆盖需要的键即可。SmartAdmin 的选择器封装就是把 vue-i18n 的文案算成 `labels` 传进去，代码在内核包的 `components/IconPicker/index.vue`。接 `computed` 之后，切语言时选择器文案跟着切：

```vue
<SmartIconPicker v-model="icon" :labels="{ placeholder: '选择一个图标', title: '图标' }" />
```

可覆盖的键：`placeholder` / `title` / `search` / `local` / `online` / `onlinePlaceholder` / `use` / `offlineHint` / `loading` / `empty` / `more`。`more` 带 `{n}` 占位，渲染时替换成数量。

## 在 SmartAdmin 之外用它

这是个独立包，也能装进别的 Vue 3 + Naive UI 项目。它不打包 Vue 和 Naive UI，以 peer 依赖提供：`vue ^3.3`、`naive-ui ^2.34`、`@iconify/vue ^4 || ^5`。样式由组件自动注入，不用单独 import CSS。它仅限浏览器，因为用到 `navigator.onLine` 和 `v-html`。而且选择器必须放在应用的 `<n-config-provider>` 内，主题变量才能解析。

```bash
npm i smart-naive-icon
```

`SmartIconPicker` 的完整 props（`collections` / `localIcons` / `cap` / `clearable` 等）、`SmartIcon` 的 API，以及 SSR / Nuxt 注意事项，见 [包 README](https://github.com/SmartCode-X/smart-naive-icon/blob/main/README.md)。
