import { computed, createApp, type App } from 'vue'
import { createPinia, type Pinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import type { RouteRecordRaw } from 'vue-router'
import { SMART_TABLE_DEFAULTS, createSmartTableDefaults } from 'smart-naive-table'
import AppRoot from '#/App.vue'
import { router } from '#/router'
import { i18n, registerLocales, type ExtModule } from '#/locales'
import { registerMenuTitles } from '#/locales/menuTitle'
import { registerViews, type ViewLoader } from '#/router/viewRegistry'
import { vAuth } from '#/directives/auth'
import { setupIcons } from '#/lib/icons'
import { reloadOnChunkError, reloadOnce } from '#/lib/chunkReload'
import { runtime, type BrandOptions } from '#/lib/runtime'
import { useSite } from '#/composables/useSite'
import { trimTrailingSlashes } from '#/utils/url'

/**
 * 一个插件(或应用自身)能往内核里注册的东西。页面、语言包、图标这类文件集合由消费方在自己的代码里
 * 写 import.meta.glob、把结果交进来 —— 内核是预编译的包,自己扫不到消费方的文件。
 */
export interface SmartAdminPlugin {
  /** 页面表:`import.meta.glob('./views/**\/*.vue')`。键取 `/views/` 之后去掉 .vue(如 system/user/index),同名覆盖先注册者。 */
  views?: Record<string, ViewLoader>
  /** 语言包:`import.meta.glob('./locales/ext/*\/*.ts', { eager: true })`,键尾形如 `<locale>/<命名空间>.ts`,按命名空间深合并。 */
  locales?: Record<string, ExtModule>
  /** 布局壳之外的顶级静态路由(整屏看板、独立打印页等)。 */
  routes?: RouteRecordRaw[]
  /** 菜单 path → i18n key(存量库菜单标题是中文时用)。 */
  menuTitles?: Record<string, string>
  /** 本地 SVG 图标:`import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })`。 */
  icons?: Record<string, string>
  /** 需要 app 实例的注册动作(registerHeaderTool / registerMenuBadge / 自己的 app.use 等),在 mount 前调用。 */
  install?: (app: App) => void
}

export interface SmartAdminOptions extends SmartAdminPlugin {
  /** 插件按顺序注册,后者覆盖前者;应用自身(本对象的 views/locales 等)最后注册,优先级最高。 */
  plugins?: SmartAdminPlugin[]
  /** API 根地址;默认空串 = 同源。跨源时后端还要配 SmartAdmin:Api:Cors:AllowedOrigins。 */
  apiBase?: string
  /** 登录页页脚展示的版本号;默认内核版本。 */
  version?: string
  /** 开发态(登录页预填超管账号、设置抽屉「复制配置」);消费方传 import.meta.env.DEV。 */
  dev?: boolean
  /** 品牌默认值:sys.site.* 配置有值时以配置为准,为空时用这里。 */
  brand?: BrandOptions
  /** SmartTable 全局默认。 */
  table?: { density?: 'comfortable' | 'compact'; pageSizes?: number[] }
}

export interface SmartAdminApp {
  app: App
  router: typeof router
  pinia: Pinia
  i18n: typeof i18n
  /** 挂载到选择器或元素;返回 Vue app 实例。 */
  mount: (target: string | Element) => App
}

/**
 * 组装内核应用:写入运行期配置 → 并入插件与应用的页面/文案/路由/图标 → 建 pinia/router/i18n → 返回可挂载的 app。
 * 按 内核内置 < 插件(按数组顺序)< 应用自身 的顺序注册,同 key 后者覆盖前者。结果与后端相同(应用压过内核),
 * 机制相反:后端 TryAdd 是消费方先注册者胜。
 */
export function createSmartAdmin(options: SmartAdminOptions = {}): SmartAdminApp {
  // apiBase 写入即生效:各处客户端在下一次调用时按新地址重建(见 api/client.ts 的 followApiBase)
  runtime.apiBase = trimTrailingSlashes(options.apiBase ?? '')
  if (options.version) runtime.version = options.version
  runtime.dev = !!options.dev
  runtime.brand = options.brand ?? {}
  if (options.brand?.title) useSite().site.title = options.brand.title

  const layers: SmartAdminPlugin[] = [...(options.plugins ?? []), options]
  const icons: Record<string, string> = {}
  for (const layer of layers) {
    if (layer.views) registerViews(layer.views)
    if (layer.locales) registerLocales(layer.locales)
    if (layer.menuTitles) registerMenuTitles(layer.menuTitles)
    if (layer.icons) Object.assign(icons, layer.icons)
    for (const route of layer.routes ?? []) router.addRoute(route)
  }
  setupIcons(icons) // 注册离线图标集(ph 子集同步入库)+ 本地 SVG,不预热整集

  // 全局兜底:没有这两个监听的话,未捕获异常和发版后旧 chunk 404 都是白屏,控制台之外不留痕迹。
  // 渲染期异常由内容区的 ErrorBoundary 收口,这里管它够不着的两类:游离的 Promise 拒绝与预加载失败。
  window.addEventListener('unhandledrejection', e => {
    if (reloadOnChunkError(e.reason)) e.preventDefault()
  })
  // Vite 预加载新 chunk 失败时派发;能走到这个事件就一定是 chunk 问题,不必再判文案。
  window.addEventListener('vite:preloadError', e => {
    if (reloadOnce()) e.preventDefault()
  })

  const pinia = createPinia()
  pinia.use(piniaPluginPersistedstate)

  const app = createApp(AppRoot)
  app.use(pinia) // 必须在 router 之前:守卫用到 store
  // 深浅色默认 themeScheme:'auto' → 首访自动跟随系统,用户手选 light/dark 后固定(见 stores/app.ts)。
  app.use(router)
  app.use(i18n)
  app.directive('auth', vAuth)
  app.config.errorHandler = (err, _instance, info) => {
    if (reloadOnChunkError(err)) return
    console.error('[SmartAdmin] 未捕获异常', info, err)
  }

  // SmartTable 全局默认:labels 一次注入,各页面不用手传 :labels(切语言即时生效)。
  // 只能在这里注入一次:Vue 的 inject 取最近的 provide、不合并 —— 在 App.vue 或任何祖先组件里
  // 再 provide 一次 SMART_TABLE_DEFAULTS,会把这里的 labels/density 整份挡掉。要加默认值就往下面这个对象里加。
  app.provide(
    SMART_TABLE_DEFAULTS,
    createSmartTableDefaults({
      // 表格工具栏密度默认使用紧凑档;已有 storage-key 的用户选择仍按本地缓存优先。
      density: options.table?.density ?? 'compact',
      // 内置兜底是 [10,20,50],后台列表动辄上千行,翻页翻不过来。
      // 上限 200:内核 Api:MaxPageSize 的默认值,选更大的后端会回 48001。
      // 注意 SmartTable 的**初始**每页条数走 default-page-size prop(不在这份可注入的 defaults 里),
      // 没传那个 prop 的页面初始仍是 10,条数选择器会显示一个不在列表里的值。
      pageSizes: options.table?.pageSizes ?? [20, 50, 100, 200],
      labels: computed(() => {
        void i18n.global.locale.value // 触发 locale 依赖收集
        const t = i18n.global.t
        return {
          search: t('common.search'),
          reset: t('common.reset'),
          refresh: t('table.refresh'),
          density: t('app.density'),
          densityComfortable: t('app.comfortable'),
          densityCompact: t('app.compact'),
          columnSettings: t('table.columnSettings'),
          columnSettingsReset: t('table.columnSettingsReset'),
          fixedLeft: t('table.fixedLeft'),
          fixedRight: t('table.fixedRight'),
          fixedNone: t('table.fixedNone'),
          expand: t('table.expand'),
          collapse: t('table.collapse'),
        }
      }),
    }),
  )

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
