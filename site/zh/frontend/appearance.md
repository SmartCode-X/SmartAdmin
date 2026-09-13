# 主题与图标

后台的外观没有一处写在组件 props 里。颜色经一层 CSS 变量下发，图标在启动时一次性离线注册好。换主色、补暗色、塞自己的 SVG，改的都只是这两处的输入。

## 四层 token，业务只碰角色令牌

SmartAdmin 的外观由 CSS 自定义属性驱动，不是组件 props。变量都在内核包的 `styles/tokens.css`，随 `smart-admin-web/style.css` 发给应用，分成四层：

- **原语**：灰阶（`--color-gray-50…900`）、四语义色的基色。固定值，不随主题翻转。
- **角色令牌**：`--color-bg-*`、`--color-text-*`、`--color-border*`、`--color-fill*`、`--color-primary*`、`--color-mask`。语义化命名，亮色值放在 `:root`，暗色覆盖放在 `:root[data-theme="dark"]`。**业务代码只消费这一层。**
- **度量**：字号、间距、圆角。与主题无关，只在 `:root` 出现一次。
- **阴影**：亮色在 `:root`，暗色单独覆盖（深底需要更重的投影）。

「业务只碰角色令牌」不是规矩，是省事。假如某段业务 CSS 直接写了 `--color-gray-900` 当文字色，暗色主题就翻不到它。灰阶是固定原语，不随 `data-theme` 变，于是深底上顶着一行黑字。只有角色令牌在亮暗两套里各存一份值。写 `--color-text-primary` 这一行 CSS，两个主题下都对，因为翻的是 token 背后的值，不是你的样式。换肤也是这个道理。整套换配色，也只改角色令牌那一层的覆盖块，原语和度量原封不动。

## 明暗：首访跟随系统，手动切换后记住

`themeScheme` 有三态：`'light'`、`'dark'`、`'auto'`（默认），它在 `app` store 里，也就是 `stores/app.ts`。`auto` 下，`isDark` getter 通过 VueUse 的 `usePreferredDark` 响应式读取系统偏好。所以首访会自动匹配系统深浅色，系统主题一变也实时联动。点顶栏的切换按钮 `toggleDark()`，或者调 `setThemeScheme()`，就会落到明确的 `'light'` 或 `'dark'`。这个选择随 store 持久化进 `localStorage`，键是 `app`，此后不再跟随系统。

## 主色与密度

同一个 store 里还有两个用户可调的开关，和 `themeScheme` 一起持久化：

- `accent`：品牌主色，从 6 个候选里选（`theme/accents.ts`：青绿 `#14B8A6` 默认，其余候选含靛蓝 `#646CFF`——同时是 Logo 固定不变的品牌色）。换主色即重算 `--color-primary*`。
- `density`：`'comfortable'` / `'compact'`，落到 `<html>` 的 `data-density`，联动表格行高与卡片内边距。

## 从 token 到 Naive UI

手写 CSS 直接读 tokens，但 Naive UI 组件不认 CSS 变量，它要一个 JS 对象 `GlobalThemeOverrides`。桥在 `buildThemeOverrides()`，也就是 `theme/naive-theme.ts`。它用 `getComputedStyle` 把同一批 CSS 变量读出来，映到 Naive 的 `common.*`：`primaryColor`←`--color-primary`、`bodyColor`←`--color-bg-body`、`borderRadius`←`--radius-md`，依此类推。两边读的是同一份值，所以手写样式和 Naive 组件永远不会各显各的色。

主色是唯一不直接读、而是算出来的一档。6 个候选主色，不可能每个都在 `tokens.css` 里预写 hover/pressed/light 四态。所以只存一个 `accent`，其余状态由 `mix(a, b, t)` 派生。`mix` 在 `theme/mix.ts`，把两色按 `t∈[0,1]` 线性插值。亮色这样算：`hover = mix(primary, #FFF, .16)`、`pressed = mix(primary, #000, .18)`。暗色不一样，先把 accent 往白里提亮一档 `mix(accent, #FFF, .18)`，再往下派生，免得靛蓝压在深底上发闷。

这些都落地在 `composables/useTheme.ts` 的 `useTheme()` 里，盯着 `app.isDark`、`accent`、`density`、`grayscale` 四样，任意一个变就动手。往 `<html>` 打 `data-theme`、`data-density` 和灰阶用的 `data-gray`，把派生出的 `--color-primary*` 写进 `document.documentElement`，让消费 token 的手写 CSS 立即换色，再重建 Naive 的 `themeOverrides`。`App.vue` 把结果接到 `<n-config-provider :theme-overrides>`，包住整个应用。

::: tip 完整 token 表
上面够你换主色、加暗色、判断该改哪一层。完整的令牌清单、语义徽章派生、`token → Naive` 全映射表，见 [`web/DESIGN.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/DESIGN.md)。
:::

## Logo：一处配置，全站生效

侧栏、顶栏、登录页、应用选择页的 Logo 都由 `SmartLogo` 渲染。取值按顺序来：后台配置 `sys.site.logo` 有图片地址就用这张图，否则用 `createSmartAdmin({ brand: { logo } })` 传的组件或图片地址，都没有才是内置矢量标。`brand` 是配置为空时的默认，站点信息到达之前的首帧也用它。

## 图标离线注册

图标是**离线渲染**的。几套 Iconify 图标集，加上你自己的本地 SVG，启动时统一注册一次。之后在任意组件里，通过薄封装 `AppIcon` 使用，也可以在菜单管理里用 `IconPicker` 交互式挑选。

`setupIcons()`（内核包的 `lib/icons.ts`）由 `createSmartAdmin()` 调一次，靠 `smart-naive-icon` 的 `setupSmartIcon` 注册两类来源：

- **离线 Iconify 集**：`ph`（Phosphor，默认集）、`lucide`（Lucide）、`ep`（Element Plus）、`ant-design`（Ant Design）。每套是独立的懒加载 `@iconify-json/<prefix>` chunk，第一次用到才加载。`ph` 另有子集在启动时同步注册，见下一节。
- **本地 SVG**：内核自带的在包里的 `assets/svg/`，应用自己的以原始字符串 glob 进来，交给 `createSmartAdmin()` 的 `icons` 选项：

```ts
createSmartAdmin({
  icons: import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
})
```

  文件名去掉 `.svg` 就是图标名，例如 `src/assets/svg/star.svg` 成为可选的 `local:star`。

四套内置图标集和本地 SVG 都打进了应用本身，渲染它们不请求外部 CDN，比如 `api.iconify.design`。但有个前提：图标只能从 `ph`/`lucide`/`ep`/`ant-design`/`local:` 里选。选择器的「在线」页能输入任意 Iconify 名，可那些名字没打进包，离线环境里出不来。

## `ph` 子集：内核一份，应用一份

整套 `ph` 有 9000 多个图标，gzip 后约 946 KB，所以启动时同步注册的只是子集，子集外的名字第一次渲染时才由 `AppIcon` 懒加载整套。内核那份随包发布，应用那份用包里带的 `smart-admin-icons` 命令从自己的 `src` 生成：

```bash
npm run gen:icons               # 模板里的脚本，即 smart-admin-icons：扫 src，写出 src/assets/icons/ph-subset.json
npx smart-admin-icons --check   # CI 用：产物过期或有拼错的图标名时非 0 退出
```

生成的 JSON 经 `iconSets` 交给内核，模板已经接好：

```ts
import phSubset from './assets/icons/ph-subset.json'

createSmartAdmin({
  iconSets: [phSubset],
})
```

页面里用了新的 `ph:*` 名字，就重跑一次 `npm run gen:icons`。漏跑了图标照样出得来，只是退回懒加载整套。扫描范围是 `src` 下的 `.vue`、`.ts`、`.tsx`、`.js`、`.jsx`、`.mjs`，跳过 `*.spec.*`、`*.test.*` 和 `node_modules`。种子菜单这类名字不在页面源码里，写进 `src` 下任意一个 `.ts` 就会被扫进去，比如导出一个数组。

应用那份不剔除内核子集里已有的名字。两份重名无害，同名图标的数据本来就相同。要是按内核的子集剔除，应用提交的这份 JSON 就绑死在某个内核版本上。内核一升级，`--check` 就可能报过期，升级也就不再是只改版本号。

## 在组件里用图标

`AppIcon`（内核包的 `components/AppIcon.vue`）封装了 `smart-naive-icon` 的 `SmartIcon`，是全站渲染图标的标准方式：

```vue
<script setup lang="ts">
import { AppIcon } from 'smart-admin-web'
</script>

<template>
  <AppIcon icon="ph:house-duotone" />
  <AppIcon icon="local:star" :size="20" />
</template>
```

`icon` 是 `prefix:name` 字符串，本地 SVG 用 `local:name`，默认大小 `18`。`icon` 为空或者找不到时，`AppIcon` 兜底成 `ph:dot-outline-duotone`。这和侧栏菜单**页面叶子**未设图标时是一致的。目录节点另有兜底，是 `ph:folder-duotone`，这段在 `composables/useLayoutMenu.ts`。

## 在菜单管理里选图标

`IconPicker` 是应用里的选择器，用在**系统管理 → 菜单管理**的菜单 `icon` 字段上：封装了 `smart-naive-icon` 的 `SmartIconPicker`，注入 SmartAdmin 自己的 vue-i18n 文案，还复用 `setupIcons()` 已经全局注册好的图标集，源码在内核包的 `components/IconPicker/index.vue`。所以 `ph` 成了首个、也是默认的 Tab，调用处不用再配置。

::: tip 选择器完整 API
这里只讲图标在应用里怎么接入。选择器组件本身由独立包提供。多图标库 Tab、注册本地 SVG、`labels`/i18n、`v-model` 约定，这些完整 API 见 [SmartIcon 与 SmartIconPicker](/zh/components/smart-icon)。
:::
