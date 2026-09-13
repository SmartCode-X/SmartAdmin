import { describe, expect, it } from 'vitest'
import { fmtBytes, fmtDateTime, operatorText } from './format'

describe('fmtDateTime', () => {
  it('截断到秒 / 到分,T 换空格', () => {
    expect(fmtDateTime('2026-09-06T11:22:33.4567')).toBe('2026-09-06 11:22:33')
    expect(fmtDateTime('2026-09-06T11:22:33', { seconds: false })).toBe('2026-09-06 11:22')
  })

  it('空值走占位', () => {
    expect(fmtDateTime(null)).toBe('')
    expect(fmtDateTime(undefined, { empty: '—' })).toBe('—')
    expect(fmtDateTime('   ', { empty: '—' })).toBe('—')
  })

  // 后端串是服务器本地时区(无 Z),交给 Date 解析会被当 UTC 平移几小时 —— 这里只准截断。
  it('不做时区换算', () => {
    expect(fmtDateTime('2026-01-01T00:30:00')).toBe('2026-01-01 00:30:00')
  })
})

describe('fmtBytes', () => {
  it('按二进制单位进位', () => {
    expect(fmtBytes(0)).toBe('0 B')
    expect(fmtBytes(512)).toBe('512 B')
    expect(fmtBytes(1024)).toBe('1.00 KB')
    expect(fmtBytes(1536)).toBe('1.50 KB')
    expect(fmtBytes(1024 ** 3)).toBe('1.00 GB')
  })

  it('超出 TB 不再进位,负数/空值当 0', () => {
    expect(fmtBytes(1024 ** 5)).toBe('1024.00 TB')
    expect(fmtBytes(-1)).toBe('0 B')
    expect(fmtBytes(null)).toBe('0 B')
  })
})

describe('operatorText', () => {
  it('姓名优先,只有 Id 时回退 Id,都没有走占位', () => {
    expect(operatorText({ operatorName: '张三', operatorId: 7 })).toBe('张三')
    expect(operatorText({ operatorName: null, operatorId: 7 })).toBe('7')
    expect(operatorText({ operatorName: '', operatorId: null })).toBe('—')
  })
})
