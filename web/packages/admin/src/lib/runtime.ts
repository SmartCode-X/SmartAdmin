import type { Component } from 'vue'

/** 品牌默认值:sys.site.* 配置有值时以配置为准,为空时用这里。 */
export interface BrandOptions {
  /** 站点标题默认值;siteInfo 里 sys.site.title 有值时被覆盖 */
  title?: string
  /** 组件或图片地址;不传则用内置 SmartLogo 矢量标 */
  logo?: Component | string
}

/**
 * 运行期配置:由 createSmartAdmin 在挂载前写入,内核各处读这里而不读 import.meta.env ——
 * 库预编译时 import.meta.env 会被静态替换成库自己构建时的值,消费方改不了。
 */
export const runtime = {
  /** API 根地址;空串 = 同源(dev 由 Vite 代理,生产由反代或后端托管 dist) */
  apiBase: '',
  /** 登录页页脚展示的版本号;默认内核版本,消费方可覆盖为应用版本 */
  version: typeof __SMART_ADMIN_VERSION__ !== 'undefined' ? __SMART_ADMIN_VERSION__ : '',
  /** 开发态:登录页预填超管账号、设置抽屉显示「复制配置」 */
  dev: false,
  brand: {} as BrandOptions,
}
