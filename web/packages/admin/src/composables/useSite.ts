import { reactive } from 'vue'
import { configApi } from '#/api'
import { runtime } from '#/lib/runtime'

// 站点品牌信息(匿名 GET /sys/config/site 下发)。App.vue 启动、登录页各皮肤、登录后框架(侧栏/顶栏/水印)
// 共用同一份响应式数据,只拉一次(模块级缓存);改配置保存后 loadSite(true) 强制重取即时生效。
export interface SiteInfo {
  title: string
  subtitle: string
  copyright: string
  copyrightUrl: string
  logo: string
  captchaEnabled: boolean
  smsLoginEnabled: boolean
}

// title 初值给内置名,防 siteInfo 到达前品牌词首帧空白;createSmartAdmin({ brand.title }) 会在挂载前改写它。
const site = reactive<SiteInfo>({
  title: 'SmartAdmin',
  subtitle: '',
  copyright: '',
  copyrightUrl: '',
  logo: '',
  captchaEnabled: false,
  smsLoginEnabled: false,
})

let inflight: Promise<void> | null = null

export function loadSite(force = false): Promise<void> {
  if (inflight && !force) return inflight
  inflight = configApi
    .siteInfo()
    .then(s => {
      if (s.title) site.title = s.title
      site.subtitle = s.subtitle ?? ''
      site.copyright = s.copyright ?? ''
      site.copyrightUrl = s.copyrightUrl ?? ''
      site.logo = s.logo ?? ''
      site.captchaEnabled = !!s.captchaEnabled
      site.smsLoginEnabled = !!s.smsLoginEnabled
    })
    .catch(() => {
      // 拉取失败保留内置默认(title=SmartAdmin),不阻塞登录/渲染
    })
  return inflight
}

/** 版本号不走后端配置:默认是内核版本(库构建期常量),消费方经 createSmartAdmin({ version }) 换成应用版本。 */
export function useSite() {
  return { site, appVersion: runtime.version, loadSite }
}
