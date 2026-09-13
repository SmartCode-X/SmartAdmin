// 菜单角标登记表(未读数 / 待办数)。
// 不做成 MenuNode 上的字段:菜单树是后端拉的,每次重拉都会把前端算出来的动态值冲掉;
// 这里按路由 path 登记,渲染在 useLayoutMenu 产出的 MenuOption.extra 上,
// 侧栏 / 顶栏 / 二级 / 移动抽屉四处共用同一份。
import { reactive, toValue, type MaybeRefOrGetter } from 'vue'

/** 角标值。0 / '' / null / undefined 一律不渲染——未读为零时不该在菜单上留个空圈。 */
export type MenuBadgeValue = number | string | null | undefined

const badges = reactive(new Map<string, () => MenuBadgeValue>())

/**
 * 给菜单项挂角标,返回注销函数。
 * <p>`path` 用菜单的路由 path(与 MenuNode.path 同一个值)。value 可传 ref / getter:
 * 值变了角标自己跟着变,不必重新登记。同一 path 重复登记以最后一次为准。</p>
 */
export function registerMenuBadge(
  path: string,
  value: MaybeRefOrGetter<MenuBadgeValue>,
): () => void {
  const read = () => toValue(value)
  badges.set(path, read)
  return () => {
    if (badges.get(path) === read) badges.delete(path)
  }
}

/** 取某菜单项当前的角标值(useLayoutMenu 渲染用)。 */
export function menuBadge(path: string): MenuBadgeValue {
  return badges.get(path)?.()
}
