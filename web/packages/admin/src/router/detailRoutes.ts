import { router, registerDynamic } from '#/router'
import { namedPage } from '#/router/namedPage'
import { detailViews } from '#/router/viewRegistry'

// 约定式详情路由:约定「views 下任意 `detail.vue` = 一个按记录 id 的详情页」。
//   views/system/user/detail.vue(页面表 key system/user/detail)→ 路由 /system/user/:id/detail,名 detail-system-user
// 走动态注册(而非写死进 routes.ts):由 buildRoutesForModule 在菜单路由重建处一并调用,
// 故每次登录/切应用/F5 重建时详情路由随菜单路由一起复活 —— 动态且刷新/深链安全。
// 消费方加详情页:自己 views 下丢一个 `<模块路径>/detail.vue`,随 createSmartAdmin({ views }) 进页面表即可。
// 局限(需自定义参数名 / 一页多详情)时再退回显式静态路由。见 skills/create-page-variant/detail.md。

/** 扫描 detail.vue 约定,把详情路由挂到 layout 壳下。幂等:重复调用先删同名再加。 */
export function registerDetailRoutes(): void {
  for (const [key, loader] of detailViews()) {
    const base = `/${key.slice(0, -'/detail'.length)}` // → /system/user
    const name = 'detail' + base.replace(/\//g, '-') // → detail-system-user
    if (router.hasRoute(name)) router.removeRoute(name)
    router.addRoute('layout', {
      path: `${base}/:id/detail`,
      name,
      component: namedPage(name, loader),
      // title 用通用「详情」兜底;页面加载后由 useTabTitle 覆盖成记录名。noCache:详情只读,不进 keep-alive。
      meta: { title: 'common.detail', noCache: true },
    })
    registerDynamic(name)
  }
}
