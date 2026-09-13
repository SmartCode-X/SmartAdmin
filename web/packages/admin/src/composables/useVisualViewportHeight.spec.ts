import { afterEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import { useVisualViewportHeight } from './useVisualViewportHeight'

/** 造一个可控的 visualViewport(happy-dom 没有实现它)。 */
function stubViewport(height: number) {
  const listeners: Record<string, Array<() => void>> = {}
  const vv = {
    height,
    addEventListener: (type: string, fn: () => void) => void (listeners[type] ??= []).push(fn),
    removeEventListener: (type: string, fn: () => void) => {
      listeners[type] = (listeners[type] ?? []).filter(f => f !== fn)
    },
  }
  vi.stubGlobal('visualViewport', vv)
  return {
    vv,
    emit(type: string) {
      for (const fn of listeners[type] ?? []) fn()
    },
    listenerCount: (type: string) => (listeners[type] ?? []).length,
  }
}

afterEach(() => {
  document.documentElement.style.removeProperty('--vvh')
})

describe('useVisualViewportHeight', () => {
  it('把可视高度写进 --vvh,并随 resize 更新(软键盘弹起就是这条路径)', () => {
    const vp = stubViewport(800)
    const scope = effectScope()
    try {
      const h = scope.run(() => useVisualViewportHeight())!
      expect(h.value).toBe(800)
      expect(document.documentElement.style.getPropertyValue('--vvh')).toBe('800px')

      vp.vv.height = 420 // 键盘弹起
      vp.emit('resize')
      expect(h.value).toBe(420)
      expect(document.documentElement.style.getPropertyValue('--vvh')).toBe('420px')
    } finally {
      scope.stop()
    }
  })

  // iOS 键盘弹起是「视口不变、整页往上顶」,只有 scroll 反映得出来 —— 少听一个事件就永远差一截。
  it('也听 scroll,并在作用域销毁时退订 + 清变量', () => {
    const vp = stubViewport(900)
    const scope = effectScope()
    const h = scope.run(() => useVisualViewportHeight())!
    expect(vp.listenerCount('scroll')).toBe(1)

    vp.vv.height = 500
    vp.emit('scroll')
    expect(h.value).toBe(500)

    scope.stop()
    expect(vp.listenerCount('resize')).toBe(0)
    expect(vp.listenerCount('scroll')).toBe(0)
    expect(document.documentElement.style.getPropertyValue('--vvh')).toBe('')
  })

  it('没有 visualViewport 的环境退回 innerHeight,不炸', () => {
    vi.stubGlobal('visualViewport', undefined)
    const scope = effectScope()
    try {
      const h = scope.run(() => useVisualViewportHeight())!
      expect(h.value).toBe(window.innerHeight)
    } finally {
      scope.stop()
    }
  })
})
