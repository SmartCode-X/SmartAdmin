import type { Component } from 'vue'

/** 页面懒加载器:import.meta.glob 的每个值就是一个。 */
export type ViewLoader = () => Promise<Component | { default: Component }>

// 页面表:key 即菜单 component 字段的取值(如 system/user/index)。内核内置页在本模块加载时登记,
// 消费方与插件经 registerViews 并入;同名覆盖,谁后注册谁生效(createSmartAdmin 里插件在前、应用最后)。
// 每个 loader 引用稳定(glob 每文件一个函数),namedPage 靠它判断 keep-alive 缓存要不要失效。
const views: Record<string, ViewLoader> = {}

/** glob 键 → 页面 key:取最后一个 `/views/` 之后、去掉 .vue(`./views/system/user/index.vue` → `system/user/index`)。 */
export function normalizeViewKey(path: string): string {
  const i = path.lastIndexOf('/views/')
  const rel = i >= 0 ? path.slice(i + '/views/'.length) : path.replace(/^\.?\/+/, '')
  return rel.replace(/\.vue$/, '')
}

/** 并入一批页面(键可以是 glob 原始路径,也可以是已规范化的 key)。 */
export function registerViews(map: Record<string, ViewLoader>): void {
  for (const [key, loader] of Object.entries(map)) views[normalizeViewKey(key)] = loader
}

export function getView(key: string): ViewLoader | undefined {
  return views[key]
}

export function hasView(key: string): boolean {
  return key in views
}

/**
 * 菜单 component 字段的全部合法取值,供菜单管理表单的「组件路径」下拉:
 * 手敲错一个字符时 buildRoutesForModule 会保留路径并落到 MissingRoute 诊断页;下拉仍是第一道防线。
 */
export function viewComponentPaths(): string[] {
  return Object.keys(views).toSorted()
}

/** 约定式详情页:key 以 `/detail` 结尾的页面(views/<模块>/detail.vue)。 */
export function detailViews(): Array<[key: string, loader: ViewLoader]> {
  return Object.entries(views).filter(([k]) => k.endsWith('/detail'))
}

// 内核内置页
registerViews(import.meta.glob('../views/**/*.vue') as Record<string, ViewLoader>)
