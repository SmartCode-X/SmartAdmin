import { describe, expect, it, vi } from 'vitest'
import { normalizeViewKey, hasView, viewComponentPaths } from './viewRegistry'

const loader = () => Promise.resolve({ default: {} })

describe('normalizeViewKey', () => {
  it('取最后一个 /views/ 之后并去掉 .vue 后缀', () => {
    expect(normalizeViewKey('./views/system/user/index.vue')).toBe('system/user/index')
    expect(normalizeViewKey('../views/a/b.vue')).toBe('a/b')
    expect(normalizeViewKey('/src/views/x/y.vue')).toBe('x/y')
  })

  it('路径里出现多个 /views/ 时取最后一个', () => {
    expect(normalizeViewKey('./src/views/deep/views/z.vue')).toBe('z')
  })

  it('已规范化的 key 原样返回', () => {
    expect(normalizeViewKey('system/user/index')).toBe('system/user/index')
  })
})

describe('内核内置页', () => {
  it('已登记内置页', () => {
    expect(hasView('system/user/index')).toBe(true)
  })

  it('viewComponentPaths 已排序且包含内置页', () => {
    const paths = viewComponentPaths()
    expect(paths).toEqual(paths.toSorted())
    expect(paths).toContain('system/dict/index')
    expect(paths).toContain('error/404')
  })
})

describe('registerViews', () => {
  // 会往模块级 views 表里写入,污染后续用例——resetModules + 动态 import 换一份干净模块。
  it('同名覆盖:后注册者胜', async () => {
    vi.resetModules()
    const mod = await import('./viewRegistry')

    mod.registerViews({ 'system/user/index': loader })

    expect(mod.getView('system/user/index')).toBe(loader)
  })
})

describe('detailViews', () => {
  it('只返回 /detail 结尾的 key', async () => {
    vi.resetModules()
    const mod = await import('./viewRegistry')

    mod.registerViews({ 'demo/order/detail': loader, 'demo/order/index': loader })
    const keys = mod.detailViews().map(([key]) => key)

    expect(keys).toContain('demo/order/detail')
    expect(keys).not.toContain('demo/order/index')
  })
})
