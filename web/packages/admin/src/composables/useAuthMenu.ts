// 后端菜单树 → vue-router 动态路由的物化层(决策部分在 authMenuRoute.ts,这里只管注册)。
// 动态路由只活在内存里,F5/深链后是空的,所以路由守卫里的 useModule().enterInitial() 会再调一次
// buildRoutesForModule 重建 —— 本文件被重复调用是常态,resetRouter + hasRoute/removeRoute 保证幂等。
// 坑:菜单 component 字段必须落在页面表(router/viewRegistry)里;拼错不会静默 404,
//   而是注册成 MissingRoute 诊断页,这样配错的人能看见到底缺哪个文件。
import { router, resetRouter, registerDynamic } from '#/router'
import { namedPage } from '#/router/namedPage'
import { registerDetailRoutes } from '#/router/detailRoutes'
import { getView, viewComponentPaths } from '#/router/viewRegistry'
import { useAuthStore } from '#/stores/auth'
import { personalApi } from '#/api'
import { describeMenuRoute } from './authMenuRoute'
import type { MenuNode } from '#/types/menu'

export { registerViews, viewComponentPaths, type ViewLoader } from '#/router/viewRegistry'

function flatten(nodes: MenuNode[]): MenuNode[] {
  return nodes.flatMap(n => [n, ...(n.children?.length ? flatten(n.children) : [])])
}

/** 拉某应用的菜单树 → 重建动态路由(挂在 layout 下)。 */
export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  const viewKeys = new Set(viewComponentPaths())
  resetRouter()
  for (const node of flatten(tree)) {
    const route = describeMenuRoute(node, viewKeys)
    if (!route) continue

    if (router.hasRoute(route.name)) router.removeRoute(route.name)
    if (route.kind === 'iframe') {
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/embed/iframe.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, iframeSrc: route.iframeSrc },
      })
      registerDynamic(route.name)
      continue
    }

    if (route.kind === 'missing') {
      // 组件路径配错是运维需要在控制台看到的诊断信息
      // eslint-disable-next-line no-console
      console.warn('[menu] 缺少视图组件:', route.component)
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/error/MissingRoute.vue')),
        meta: {
          title: route.title,
          icon: route.icon,
          keepAlive: true,
          missingComponent: route.component,
        },
      })
      registerDynamic(route.name)
      continue
    }

    const loader = getView(route.viewKey)!
    router.addRoute('layout', {
      path: route.path,
      name: route.name,
      component: namedPage(route.name, loader),
      meta: { title: route.title, icon: route.icon, keepAlive: true },
    })
    registerDynamic(route.name)
  }
  // 约定式详情路由(views/**/detail.vue → /<路径>/:id/detail)随菜单路由一并注册,
  // 故 F5/深链走守卫重建时详情路由也复活(见 router/detailRoutes.ts)。
  registerDetailRoutes()
  auth.routesReady = true
}
