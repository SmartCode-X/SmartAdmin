/// <reference types="node" />
// 扫源码要读目录,tsconfig 的 types 只给 vite/client。
import { readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 内核 `SmartAdmin:Api:MaxPageSize` 的默认值。请求的每页条数超过它,后端回 48001。
 *
 * 这条守卫防的是:pageSize/Size 类参数在源码里被写成比这个上限更大的字面量——后端超限即报
 * 48001,但只在少数特定操作下才会触发,不能完全依赖运行时发现;触发不到的地方就只是静默拿到
 * 比页面显示的「每页 N」条数更少的数据,不会崩、不会红,很容易被忽略。
 *
 * 所以这里不测运行时行为,测的是「源码里不许再写出一个大于上限的每页条数」。
 */
const MAX_PAGE_SIZE = 200

const SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

/** 每页条数的几种写法:`pageSize: N` / `Size: N` / `unpagedSize: N` / `pageSizes: [...]`。 */
const SINGLE = /\b(?:pageSize|unpagedSize|Size)\s*:\s*(\d+)/g
const LIST = /\bpageSizes\s*:\s*\[([\d,\s]+)]/g

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const full = path.join(dir, name)
    if (statSync(full).isDirectory()) return sourceFiles(full)
    if (!/\.(ts|vue)$/.test(name)) return []
    if (name.endsWith('.spec.ts') || name === 'schema.d.ts') return []
    return [full]
  })
}

describe('每页条数不超过内核上限', () => {
  it(`源码里没有大于 ${MAX_PAGE_SIZE} 的每页条数`, () => {
    const offenders: string[] = []

    for (const file of sourceFiles(SRC)) {
      const text = readFileSync(file, 'utf8')
      const rel = path.relative(SRC, file).replaceAll('\\', '/')

      for (const [, n] of text.matchAll(SINGLE)) {
        if (Number(n) > MAX_PAGE_SIZE) offenders.push(`${rel}: ${n}`)
      }
      for (const [, list] of text.matchAll(LIST)) {
        for (const n of list.split(',')) {
          if (Number(n.trim()) > MAX_PAGE_SIZE) offenders.push(`${rel}: pageSizes 里的 ${n.trim()}`)
        }
      }
    }

    expect(offenders, `这些地方要的每页条数超过 ${MAX_PAGE_SIZE},后端会回 48001`).toEqual([])
  })
})
