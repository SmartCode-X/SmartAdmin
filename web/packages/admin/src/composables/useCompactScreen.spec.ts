import { describe, expect, it } from 'vitest'
import { effectScope } from 'vue'
import {
  COMPACT_MAX_HEIGHT,
  COMPACT_MAX_WIDTH,
  isCompactSize,
  useCompactScreen,
} from './useCompactScreen'

describe('isCompactSize', () => {
  it('桌面尺寸不算紧凑', () => {
    expect(isCompactSize(1920, 1080)).toBe(false)
    expect(isCompactSize(1366, 768)).toBe(false)
  })

  // 平板两种形态是同一台设备,认 UA 必然两边判错其一 —— 所以判据是尺寸。
  it('平板竖屏按宽度命中,矮屏笔记本按高度命中', () => {
    expect(isCompactSize(768, 1024)).toBe(true) // iPad 竖屏
    expect(isCompactSize(1280, 600)).toBe(true) // 矮屏笔记本
  })

  it('阈值取等号算紧凑,可按页覆盖', () => {
    expect(isCompactSize(COMPACT_MAX_WIDTH, 1080)).toBe(true)
    expect(isCompactSize(1920, COMPACT_MAX_HEIGHT)).toBe(true)
    expect(isCompactSize(1280, 720, 1400)).toBe(true)
  })
})

describe('useCompactScreen', () => {
  it('读当前窗口尺寸(happy-dom 默认 1024×768,不算紧凑)', () => {
    const scope = effectScope()
    try {
      const compact = scope.run(() => useCompactScreen())!
      expect(compact.value).toBe(false)
      // 阈值调高后同一个窗口就该算紧凑,证明它真读的是窗口尺寸而不是常量
      const tight = scope.run(() => useCompactScreen({ maxWidth: 2000 }))!
      expect(tight.value).toBe(true)
    } finally {
      scope.stop()
    }
  })
})
