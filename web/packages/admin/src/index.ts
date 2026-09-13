// 这是包 smart-admin-web 的公开 API 面:Vite 库模式以本文件为唯一入口,
// 消费方只能 `import { X } from 'smart-admin-web'`,凡消费方页面可能用到的都从这里具名导出。
// 内部件(layouts/、views/、App.vue、ReauthModal、lib/icons.ts 的 setupIcons、lib/chunkReload.ts)
// 不在此列。

// 初始化
export { createSmartAdmin } from './createSmartAdmin'
export type { SmartAdminOptions, SmartAdminPlugin, SmartAdminApp } from './createSmartAdmin'
export type { BrandOptions } from './lib/runtime'
export type { ViewLoader } from './router/viewRegistry'
export type { ExtModule } from './locales'

// 样式(库构建会把这些抽成 dist/style.css)
import './styles/tokens.css'
import './styles/index.css'
import './styles/table.css'
import './styles/layout.css'

// API 原语与内置端点
export * from './api'
export {
  client,
  createApiClient,
  DEFAULT_TIMEOUT_MS,
  LONG_TIMEOUT_MS,
  readCookie,
  refreshOnce,
  ensureAccessToken,
} from './api/client'
export type { paths as KernelPaths, components as KernelComponents } from './api/schema'

// 类型与枚举:枚举是值(ImportWizard 的 strategies 等要传 DuplicateStrategy.Skip 这类成员),不能只以 export type 导出
export * from './types/api'
export * from './types/menu'

// Stores
export { useAppStore } from './stores/app'
export { useAuthStore } from './stores/auth'
export { useDictStore, useDictOptions } from './stores/dict'
export { useTabsStore } from './stores/tabs'
export { useUserStore } from './stores/user'

// 路由
export { router, resetRouter, registerDynamic } from './router'
export { namedPage } from './router/namedPage'

// i18n
export { i18n, t, withExt, registerLocales } from './locales'
export { translateMenuTitle, menuTitleKey, registerMenuTitles } from './locales/menuTitle'

// 指令
export { vAuth } from './directives/auth'

// Composables
export { describeMenuRoute, type MenuRouteDescriptor } from './composables/authMenuRoute'
export { clearClientCache } from './composables/clearClientCache'
export { buildRoutesForModule, registerViews, viewComponentPaths } from './composables/useAuthMenu'
export { useBatchDelete, type UseBatchDeleteOptions } from './composables/useBatchDelete'
export {
  COMPACT_MAX_WIDTH,
  COMPACT_MAX_HEIGHT,
  isCompactSize,
  useCompactScreen,
} from './composables/useCompactScreen'
export { useConfirm, type ConfirmOptions } from './composables/useConfirm'
export { useFullscreenToggle, type FullscreenToggle } from './composables/useFullscreenToggle'
export {
  registerHeaderTool,
  useHeaderTools,
  type HeaderToolIcon,
  type HeaderToolComponent,
  type HeaderTool,
} from './composables/useHeaderTools'
export { useLayoutMenu } from './composables/useLayoutMenu'
export { registerMenuBadge, menuBadge, type MenuBadgeValue } from './composables/useMenuBadge'
export { useMenuFlat, type MenuLeaf } from './composables/useMenuFlat'
export { useModule } from './composables/useModule'
export { noticeActionButtonType, useNoticeAction } from './composables/useNoticeAction'
export { usePageTitle } from './composables/usePageTitle'
export {
  noticeBus,
  onRealtime,
  beginVoluntaryLogout,
  useRealtime,
  type RealtimeHandler,
} from './composables/useRealtime'
export { loadSite, useSite, type SiteInfo } from './composables/useSite'
export {
  DRAG_HANDLE_CLASS,
  useTableRowSort,
  type TableRowSortOptions,
} from './composables/useTableRowSort'
export { useTableZoom } from './composables/useTableZoom'
export { useTabTitle } from './composables/useTabTitle'
export { useTheme } from './composables/useTheme'
export { useVisualViewportHeight } from './composables/useVisualViewportHeight'

// 组件
export { default as ApiSelect } from './components/ApiSelect/index.vue'
export { default as Chart } from './components/Chart/index.vue'
export { default as LineChart } from './components/Chart/LineChart.vue'
export { default as BarChart } from './components/Chart/BarChart.vue'
export { default as PieChart } from './components/Chart/PieChart.vue'
export { default as CodeBlock } from './components/CodeBlock/index.vue'
export { default as CronEditor } from './components/CronEditor/index.vue'
export { default as DetailPage } from './components/DetailPage/index.vue'
export { default as DictCheckbox } from './components/DictCheckbox/index.vue'
export { default as DictRadio } from './components/DictRadio/index.vue'
export { default as DictSelect } from './components/DictSelect/index.vue'
export { default as DictTag } from './components/DictTag/index.vue'
export { default as ErrorBoundary } from './components/ErrorBoundary/index.vue'
export { default as ExportColumnsModal } from './components/ExportColumnsModal/index.vue'
export { default as FileUpload } from './components/FileUpload/index.vue'
export { default as FormContainer } from './components/FormContainer/index.vue'
export { default as IconPicker } from './components/IconPicker/index.vue'
export { default as ImportWizard, type ImportWizardApi } from './components/ImportWizard/index.vue'
export { default as JsonEditor } from './components/JsonEditor/index.vue'
export { default as MarkdownEditor } from './components/MarkdownEditor/index.vue'
export { default as MarkdownView } from './components/MarkdownEditor/MarkdownView.vue'
export { default as OrgTreeSelect } from './components/OrgTreeSelect/index.vue'
export { default as PasswordStrength } from './components/PasswordStrength/index.vue'
export { default as RoleSelect } from './components/RoleSelect/index.vue'
export { default as SelectTable } from './components/SelectTable/index.vue'
export { default as StatusSwitch } from './components/StatusSwitch/index.vue'
export { default as TableZoomButton } from './components/TableZoomButton/index.vue'
export { default as UserPicker } from './components/UserPicker/index.vue'
export { default as UserSelect } from './components/UserSelect/index.vue'
export { default as AppIcon } from './components/AppIcon.vue'
export { default as MenuSearch } from './components/MenuSearch.vue'
export { default as SmartLogo } from './components/SmartLogo.vue'
export { default as BrandIcon } from './components/oauth/BrandIcon.vue'

// Utils
export * from './utils/chunkUpload'
export * from './utils/download'
export * from './utils/error'
export * from './utils/format'
export * from './utils/importDup'
export * from './utils/oauthBrand'
export * from './utils/tree'
export * from './utils/ua'
export * from './utils/url'

// Theme / lib
// 内核离线图标子集(启动时同步注册):消费方可据此核对自己用到的 ph 图标是否离线可用
export { default as kernelIconSet } from './assets/icons/ph-subset.json'
export { ACCENTS, type Accent } from './theme/accents'
export { mix, derivePrimary, rgba, btnGrad, glowSh, type PrimaryRamp } from './theme/mix'
export { buildThemeOverrides } from './theme/naive-theme'
export { withXssPlugin, setupMarkdown } from './lib/markdown'
export { loadingBar, LoadingBarBridge } from './lib/loadingBar'
export {
  isSiteRelativeUrl,
  classifyNoticeAction,
  noticeActionLabel,
  type NoticeActionKind,
} from './lib/noticeActions'
