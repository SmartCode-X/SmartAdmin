// auth.menuTree → n-menu 的 MenuOption,外加混合布局下顶栏一级 / 侧栏二级的选中态联动。
// 用法:default.vue 与 AppHeader 各自 useLayoutMenu(),createSharedComposable 保证它们共享同一份选中态。
// 坑:off-menu 路由(如 /personal/*)命中不到任何一级,此时保留上一次的一级选中,别把顶栏清空。
import { computed, h, ref, watch } from 'vue'
import { NBadge, type MenuOption } from 'naive-ui'
import { createSharedComposable } from '@vueuse/core'
import AppIcon from '#/components/AppIcon.vue'
import { menuBadge } from '#/composables/useMenuBadge'
import { router } from '#/router'
import { useAuthStore } from '#/stores/auth'
import { useAppStore } from '#/stores/app'
import { translateMenuTitle } from '#/locales/menuTitle'
import { isFullscreenPath, isHttpUrl } from '#/utils/url'
import { MenuType, type MenuNode } from '#/types/menu'

/**
 * 新标签页打开的两类 key → window.open;返回是否已处理。
 * - 外链菜单:path 为 http(s) URL。
 * - 站内整屏页:path 以 `/fullscreen/` 开头(大屏看板),站内静态路由,但必须脱离布局壳整屏显示。
 */
function openIfExternal(key: string): boolean {
  if (!isHttpUrl(key) && !isFullscreenPath(key)) return false
  window.open(key, '_blank', 'noopener,noreferrer')
  return true
}

// 无图标兜底:rail/折叠态的 n-menu 只画图标,缺图标会渲染成空槽、整项像“消失”了,故永不返回 undefined。
// 走 AppIcon 而非裸 Icon:菜单图标是库里配的,子集外的名字要由它懒加载整集兜底,否则渲染不出来。
function renderIcon(name: string | undefined, fallback = 'ph:dot-outline-duotone') {
  return () => h(AppIcon, { icon: name || fallback, size: 18 })
}

// 角标(registerMenuBadge 登记的未读数/待办数)→ n-menu 的 extra 槽。
// 读值在渲染函数里做,故登记的 ref 一变角标就变,不必重算整棵 options。
// 折叠 / rail 态 n-menu 只画图标、不渲染 extra,角标随之隐藏——这是 n-menu 的行为,不另做补偿。
function renderBadge(path: string | undefined) {
  if (!path) return undefined
  return () => {
    const value = menuBadge(path)
    if (value === null || value === undefined || value === 0 || value === '') return null
    return h(NBadge, { value, max: 99 })
  }
}

// 菜单树 → n-menu options:剥按钮、丢空目录、页面叶子 key=路由 path。目录/叶子各自兜底图标。
function toOptions(nodes: MenuNode[]): MenuOption[] {
  return nodes
    .toSorted((a, b) => a.sort - b.sort)
    .filter(n => n.type !== MenuType.Button && n.visible !== false)
    .map<MenuOption | null>(n => {
      if (n.type === MenuType.Catalog) {
        const children = toOptions(n.children ?? [])
        if (!children.length) return null
        return {
          label: translateMenuTitle(n.title, n.path),
          key: `cat-${n.id}`,
          icon: renderIcon(n.icon, 'ph:folder-duotone'),
          children,
        }
      }
      return {
        label: translateMenuTitle(n.title, n.path),
        key: n.path ?? `menu-${n.id}`,
        icon: renderIcon(n.icon),
        extra: renderBadge(n.path),
      }
    })
    .filter((o): o is MenuOption => o !== null)
}

function containsKey(opt: MenuOption, path: string): boolean {
  if (opt.key === path) return true
  return ((opt.children as MenuOption[]) ?? []).some(c => containsKey(c, path))
}
function firstLeafKey(opt: MenuOption): string | undefined {
  if (typeof opt.key === 'string' && opt.key.startsWith('/')) return opt.key
  for (const c of (opt.children as MenuOption[]) ?? []) {
    const k = firstLeafKey(c)
    if (k) return k
  }
  return undefined
}

/**
 * 布局菜单派生 + 一/二级选中态联动(混合布局用)。
 * createSharedComposable → default.vue 与 AppHeader 共享同一份 selectedL1,不各自漂移。
 * 走 router.currentRoute(全局 ref),不依赖组件上下文的 useRoute。
 */
function useLayoutMenuImpl() {
  const auth = useAuthStore()
  const app = useAppStore()

  // 工作台是每个应用菜单树里的一条(Sort=0 → 排第一项),跟着应用变,不用手工 prepend。
  const menuOptions = computed<MenuOption[]>(() => {
    void app.locale // 依赖:切换语言时 i18n key 标题重算
    return toOptions(auth.menuTree)
  })

  // 一级(剥 children → n-menu 渲染为可选普通项,无展开箭头)。
  const l1Options = computed<MenuOption[]>(() =>
    menuOptions.value.map(o => ({ label: o.label, key: o.key, icon: o.icon, extra: o.extra })),
  )

  const activeKey = computed(() => router.currentRoute.value.path)
  const selectedL1 = ref<string>()

  function findActiveL1(path: string): string | undefined {
    const top = menuOptions.value.find(o => containsKey(o, path))
    return top ? String(top.key) : undefined
  }

  // 路由 → 一级同步:命中才覆写(off-menu 路由如 /personal/* 保留上次);首次 seed 头一项。
  watch(
    () => router.currentRoute.value.path,
    path => {
      const l1 = findActiveL1(path)
      if (l1) selectedL1.value = l1
      else if (selectedL1.value === undefined)
        selectedL1.value = menuOptions.value[0]?.key as string | undefined
    },
    { immediate: true },
  )
  // 菜单可能晚于首个 watch 到达(F5 重建):补 seed / 修正失效选择。
  watch(menuOptions, opts => {
    if (selectedL1.value === undefined || !opts.some(o => o.key === selectedL1.value)) {
      selectedL1.value = findActiveL1(activeKey.value) ?? (opts[0]?.key as string | undefined)
    }
  })

  const l2Options = computed<MenuOption[]>(() => {
    const top = menuOptions.value.find(o => o.key === selectedL1.value)
    return (top?.children as MenuOption[]) ?? []
  })

  function onSelect(key: string) {
    if (openIfExternal(key)) return
    if (key.startsWith('/')) router.push(key)
  }
  function onSelectL1(key: string) {
    if (openIfExternal(key)) return
    selectedL1.value = key // 先设,L2 列立即刷新,不等导航
    const top = menuOptions.value.find(o => o.key === key)
    const target = key.startsWith('/') ? key : top ? firstLeafKey(top) : undefined
    if (target) router.push(target)
  }

  return { menuOptions, l1Options, l2Options, selectedL1, activeKey, onSelect, onSelectL1 }
}

export const useLayoutMenu = createSharedComposable(useLayoutMenuImpl)
