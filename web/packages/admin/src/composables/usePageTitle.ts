// 浏览器标题与文档语言。App.vue 调一次即可。
//
// 不这样动态维护的话,document.title 只会在启动时被站点名设一次,收藏夹/多窗口/历史记录里所有页面会长得一模一样;
// <html lang> 也会硬编成 zh-CN,读屏器与浏览器翻译在英文界面下判错语言。
import { watchEffect } from 'vue'
import { useRoute } from 'vue-router'
import { useAppStore } from '#/stores/app'
import { useTabsStore } from '#/stores/tabs'
import { useSite } from '#/composables/useSite'
import { translateMenuTitle } from '#/locales/menuTitle'

export function usePageTitle(): void {
  const route = useRoute()
  const app = useAppStore()
  const tabs = useTabsStore()
  const { site } = useSite()

  watchEffect(() => {
    void app.locale // 依赖:切语言后 i18n key 标题要重算
    // 详情页用 useTabTitle 设过动态标题(记录名)时以它为准 —— 静态 meta.title 是「详情」这种通名。
    const tab = tabs.tabs.find(x => x.path === route.path)
    const raw = (tab?.titleFixed && tab.title) || (route.meta.title as string | undefined) || ''
    const page = raw ? translateMenuTitle(raw, route.path) : ''
    document.title = page ? `${page} · ${site.title}` : site.title
  })

  watchEffect(() => {
    document.documentElement.lang = app.locale
  })
}
