import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import type { MenuOption } from 'naive-ui'
import { MenuType, type MenuNode } from '#/types/menu'

const mocks = vi.hoisted(() => ({
  menuTree: [] as MenuNode[],
}))

vi.mock('#/router', () => ({
  router: { currentRoute: { value: { path: '/system/user' } } },
}))
vi.mock('#/stores/auth', () => ({ useAuthStore: () => ({ menuTree: mocks.menuTree }) }))
vi.mock('#/stores/app', () => ({ useAppStore: () => ({ locale: 'zh-CN' }) }))
// AppIcon 会拉起 smart-naive-icon 的 dist/index.css,vitest 加载不了 .css:图标不是本用例的对象,直接桩掉。
vi.mock('#/components/AppIcon.vue', () => ({ default: { render: () => null } }))

import { menuBadge, registerMenuBadge } from './useMenuBadge'
import { useLayoutMenu } from './useLayoutMenu'

function leaf(id: number, path: string): MenuNode {
  return {
    id,
    parentId: 0,
    type: MenuType.Menu,
    title: '用户管理',
    path,
    sort: id,
    visible: true,
    children: [],
  }
}

beforeEach(() => {
  mocks.menuTree = [leaf(1, '/system/user'), leaf(2, '/system/role')]
})

const extraOf = (o: MenuOption | undefined) => (o?.extra as (() => unknown) | undefined)?.()

describe('registerMenuBadge', () => {
  it('登记 → 取到值;注销 → 取不到', () => {
    const off = registerMenuBadge('/system/user', 3)
    expect(menuBadge('/system/user')).toBe(3)
    off()
    expect(menuBadge('/system/user')).toBeUndefined()
  })

  it('传 ref:值变了角标跟着变,不必重新登记', () => {
    const todo = ref(0)
    const off = registerMenuBadge('/system/user', todo)
    expect(menuBadge('/system/user')).toBe(0)
    todo.value = 12
    expect(menuBadge('/system/user')).toBe(12)
    off()
  })

  it('同 path 重复登记以最后一次为准,旧的注销函数不误删新值', () => {
    const offOld = registerMenuBadge('/system/user', 1)
    const offNew = registerMenuBadge('/system/user', 2)
    offOld()
    expect(menuBadge('/system/user')).toBe(2)
    offNew()
  })
})

describe('菜单角标接到 n-menu 的 extra 上', () => {
  it('有值渲染角标,零/未登记不渲染(未读为零不该留个空圈)', () => {
    const off = registerMenuBadge('/system/user', 5)
    const { menuOptions } = useLayoutMenu()

    const user = menuOptions.value.find(o => o.key === '/system/user')
    const role = menuOptions.value.find(o => o.key === '/system/role')

    expect(extraOf(user)).not.toBeNull()
    expect(extraOf(role)).toBeNull()

    const zero = registerMenuBadge('/system/user', 0)
    expect(extraOf(user)).toBeNull()

    zero()
    off()
  })
})
