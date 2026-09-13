// 面包屑与全局搜索共同需要的「菜单叶子清单」:把 auth.menuTree 压平成带完整标题链的叶子数组。
// 坑:按钮/隐藏/排序规则必须与 useLayoutMenu 的 toOptions 保持一致,否则搜索能搜到侧栏里看不见的页面。
import { computed } from 'vue'
import { useAuthStore } from '#/stores/auth'
import { useAppStore } from '#/stores/app'
import { menuTitleKey, translateMenuTitle } from '#/locales/menuTitle'
import { MenuType, type MenuNode } from '#/types/menu'

export interface MenuLeaf {
  title: string // 原始标题(后端串或 i18n key)
  path: string
  icon?: string
  breadcrumb: string[] // 从根到叶的标题链
}

/** auth.menuTree 扁平为叶子(供面包屑与全局搜索共用),复刻 toOptions 的按钮/隐藏/排序规则。 */
export function useMenuFlat() {
  const auth = useAuthStore()
  const app = useAppStore()

  return computed<MenuLeaf[]>(() => {
    void app.locale // 依赖:切换语言时 i18n key 标题重算
    const out: MenuLeaf[] = [] // 工作台是菜单树里的一条,不用手工注入
    const walk = (nodes: MenuNode[], trail: string[]) => {
      for (const n of nodes.toSorted((a, b) => a.sort - b.sort)) {
        if (n.type === MenuType.Button || n.visible === false) continue
        const here = [...trail, translateMenuTitle(n.title, n.path)]
        if (n.type === MenuType.Menu && n.path)
          out.push({
            title: menuTitleKey(n.title, n.path),
            path: n.path,
            icon: n.icon,
            breadcrumb: here,
          })
        if (n.children?.length) walk(n.children, here)
      }
    }
    walk(auth.menuTree, [])
    return out
  })
}
