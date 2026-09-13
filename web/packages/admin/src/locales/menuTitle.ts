// 菜单标题 → i18n key,以及「按这条规则翻出可显示文本」的单源。
// 标题本身含 '.' 视为 key;否则按路由 path 查映射表,映射不到时回退原文。
//
// 映射表是消费方接缝:存量库里的菜单标题是中文(种子只插不更新)时,经 createSmartAdmin({ menuTitles })
// 传入 path → key 表;内核自身不带映射,表为空时函数退化为原样返回。ext/<locale>/ 下的文案模块不受影响。
import { t } from '#/locales'

export const MENU_TITLE_KEYS: Record<string, string> = {}

export function registerMenuTitles(map: Record<string, string>): void {
  Object.assign(MENU_TITLE_KEYS, map)
}

export function menuTitleKey(title: string, path?: string): string {
  if (title.includes('.')) return title
  const key = path ? MENU_TITLE_KEYS[path] : undefined
  return key ?? title
}

/**
 * 菜单标题 → 可显示文本。侧栏、面包屑、全局搜索、多标签、浏览器标题必须用同一条规则,
 * 否则同一个菜单在四个地方能显示成四个样子,侧栏最容易被漏掉不翻译。
 */
export function translateMenuTitle(title: string, path?: string): string {
  const key = menuTitleKey(title, path)
  return key.includes('.') ? t(key) : key
}
