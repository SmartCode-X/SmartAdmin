import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import { useFullscreenToggle } from './useFullscreenToggle'

type Mutable = Record<string, unknown>

const root = document.documentElement as unknown as Mutable
const doc = document as unknown as Mutable

const KEYS = [
  'requestFullscreen',
  'webkitRequestFullscreen',
  'webkitRequestFullScreen',
  'exitFullscreen',
  'webkitExitFullscreen',
  'fullscreenElement',
  'webkitFullscreenElement',
]

function clearAll() {
  for (const k of KEYS) {
    delete root[k]
    delete doc[k]
  }
}

beforeEach(clearAll)
afterEach(clearAll)

const run = <T>(fn: () => T) => {
  const scope = effectScope()
  const value = scope.run(fn)!
  return { value, stop: () => scope.stop() }
}

describe('useFullscreenToggle', () => {
  it('标准 API 齐全 → 支持,进出全屏各调一次', async () => {
    const request = vi.fn(async () => {
      doc.fullscreenElement = document.documentElement
    })
    const exit = vi.fn(async () => {
      doc.fullscreenElement = null
    })
    root.requestFullscreen = request
    doc.exitFullscreen = exit

    const { value: fs, stop } = run(() => useFullscreenToggle())
    try {
      expect(fs.isSupported).toBe(true)
      expect(fs.isFullscreen.value).toBe(false)

      await fs.toggle()
      expect(request).toHaveBeenCalledTimes(1)
      expect(fs.isFullscreen.value).toBe(true)

      await fs.toggle()
      expect(exit).toHaveBeenCalledTimes(1)
      expect(fs.isFullscreen.value).toBe(false)
    } finally {
      stop()
    }
  })

  // 这条就是自研这个 composable 的全部理由:iPadOS Safari 伪装成桌面版,
  // 只有带前缀的实现,VueUse 只探标准名就把它判成不支持,按钮永远是死的。
  it('只有 webkit 前缀实现(iPadOS)时仍判为支持', async () => {
    const request = vi.fn(() => {
      doc.webkitFullscreenElement = document.documentElement
    })
    const exit = vi.fn(() => {
      doc.webkitFullscreenElement = null
    })
    root.webkitRequestFullscreen = request
    doc.webkitExitFullscreen = exit

    const { value: fs, stop } = run(() => useFullscreenToggle())
    try {
      expect(fs.isSupported).toBe(true)
      await fs.enter()
      expect(request).toHaveBeenCalledTimes(1)
      expect(fs.isFullscreen.value).toBe(true)
      await fs.exit()
      expect(exit).toHaveBeenCalledTimes(1)
      expect(fs.isFullscreen.value).toBe(false)
    } finally {
      stop()
    }
  })

  // iPhone Safari:只有 video 能全屏,元素上一个 request* 都没有 → 调用方据此把按钮藏掉。
  it('一个实现都没有(iPhone)→ 不支持,且调用不抛', async () => {
    const { value: fs, stop } = run(() => useFullscreenToggle())
    try {
      expect(fs.isSupported).toBe(false)
      await expect(fs.toggle()).resolves.toBeUndefined()
      expect(fs.isFullscreen.value).toBe(false)
    } finally {
      stop()
    }
  })

  it('外部退出(按 Esc)经 fullscreenchange 同步回来', () => {
    root.requestFullscreen = vi.fn()
    doc.exitFullscreen = vi.fn()

    const { value: fs, stop } = run(() => useFullscreenToggle())
    try {
      doc.fullscreenElement = document.documentElement
      document.dispatchEvent(new Event('fullscreenchange'))
      expect(fs.isFullscreen.value).toBe(true)

      doc.fullscreenElement = null
      document.dispatchEvent(new Event('webkitfullscreenchange'))
      expect(fs.isFullscreen.value).toBe(false)
    } finally {
      stop()
    }
  })
})
