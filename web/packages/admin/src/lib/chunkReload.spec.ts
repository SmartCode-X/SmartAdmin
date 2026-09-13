import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isChunkLoadError, reloadOnce, reloadOnChunkError } from './chunkReload'

const reload = vi.fn()

beforeEach(() => {
  sessionStorage.clear()
  reload.mockClear()
  vi.stubGlobal('location', { reload })
})

describe('isChunkLoadError', () => {
  it('认得各浏览器的动态导入失败文案', () => {
    expect(
      isChunkLoadError(new TypeError('Failed to fetch dynamically imported module: /assets/x.js')),
    ).toBe(true)
    expect(isChunkLoadError(new Error('error loading dynamically imported module'))).toBe(true)
    expect(isChunkLoadError(new Error('Importing a module script failed.'))).toBe(true)
  })

  it('普通业务错误不算', () => {
    expect(isChunkLoadError(new Error('用户不存在'))).toBe(false)
    expect(isChunkLoadError(null)).toBe(false)
  })
})

describe('reloadOnce', () => {
  // 少了这道闸,新版本身加载不了(CDN 没同步完/断网)时就是无限刷新。
  it('每个标签页只自动重载一次', () => {
    expect(reloadOnce()).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
    expect(reloadOnce()).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })
})

describe('reloadOnChunkError', () => {
  it('只对 chunk 失效动手', () => {
    expect(reloadOnChunkError(new Error('用户不存在'))).toBe(false)
    expect(reload).not.toHaveBeenCalled()
    expect(reloadOnChunkError(new Error('Failed to fetch dynamically imported module'))).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
  })
})
