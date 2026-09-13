import { describe, expect, it } from 'vitest'
import { MENU_TITLE_KEYS, menuTitleKey, registerMenuTitles } from './menuTitle'

// 内核自身不带映射,MENU_TITLE_KEYS 默认是空对象;消费方经 createSmartAdmin({ menuTitles })
// 调 registerMenuTitles 把 path → key 表注册进来。
describe('menuTitleKey', () => {
  it('映射表是普通对象,每个值都是 i18n key(含 .)', () => {
    expect(MENU_TITLE_KEYS).toBeTypeOf('object')
    for (const [path, key] of Object.entries(MENU_TITLE_KEYS)) {
      expect(path.startsWith('/'), `path ${path}`).toBe(true)
      expect(key.includes('.'), `key ${key}`).toBe(true)
    }
  })

  it('标题含 . 视为 i18n key,原样返回', () => {
    expect(menuTitleKey('menu.workbench', '/workbench')).toBe('menu.workbench')
  })

  it('映射不到时回退原文', () => {
    expect(menuTitleKey('用户管理', '/__no_such_path__')).toBe('用户管理')
    expect(menuTitleKey('用户管理')).toBe('用户管理')
  })

  it('registerMenuTitles 注册后 menuTitleKey 能查到', () => {
    registerMenuTitles({ '/__spec/path': 'menu.__spec' })
    expect(menuTitleKey('中文标题', '/__spec/path')).toBe('menu.__spec')
  })
})
