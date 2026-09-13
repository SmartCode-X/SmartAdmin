import { describe, expect, it } from 'vitest'
import zhCN from './zh-CN'
import enUS from './en-US'

// 两份语言包是手工维护的平行文件,漏译不会报错:vue-i18n 找不到键就回落到 fallbackLocale,
// 英文界面上悄悄冒出一句中文,或反过来。这条用例把「键集合完全一致」钉成硬约束。

type Node = Record<string, unknown>

/** 展平成点号路径集合;只收叶子(字符串/数组),对象继续往下钻。 */
function flatten(node: Node, prefix = '', out = new Set<string>()): Set<string> {
  for (const [key, value] of Object.entries(node)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      flatten(value as Node, path, out)
    } else {
      out.add(path)
    }
  }
  return out
}

const zh = flatten(zhCN as Node)
const en = flatten(enUS as Node)
const missing = (a: Set<string>, b: Set<string>) => [...a].filter(k => !b.has(k)).toSorted()

describe('zh-CN / en-US 键对齐', () => {
  it('en-US 不缺键', () => {
    expect(missing(zh, en)).toEqual([])
  })

  it('zh-CN 不缺键', () => {
    expect(missing(en, zh)).toEqual([])
  })

  // 占位符不一致同样是静默故障:{name} 写成 {userName} 时界面直接把大括号原样打出来。
  it('同一个键两边的占位符一致', () => {
    const placeholders = (messages: Node, path: string): string[] => {
      const value = path
        .split('.')
        .reduce<unknown>((acc, k) => (acc as Node | undefined)?.[k], messages)
      if (typeof value !== 'string') return []
      return [...value.matchAll(/\{(\w+)\}/g)].map(m => m[1]!).toSorted()
    }
    const mismatched = [...zh]
      .filter(k => en.has(k))
      .filter(
        k => placeholders(zhCN as Node, k).join(',') !== placeholders(enUS as Node, k).join(','),
      )
    expect(mismatched).toEqual([])
  })
})
