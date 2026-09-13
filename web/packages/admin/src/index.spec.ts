import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import * as entry from './index'
import { DuplicateStrategy, JobStatus } from './index'

// 包入口是消费方唯一能 import 的地方。枚举若只以类型导出,消费方写 DuplicateStrategy.Skip 会报 TS1362,
// 产物 dist/index.js 里也没有这个名字。上面的具名 import 让 typecheck 同样拦住这种回退。
describe('包入口的枚举导出', () => {
  it('types/api.ts 里的每个 enum 都以值导出', () => {
    const file = path.join(path.dirname(fileURLToPath(import.meta.url)), 'types/api.ts')
    const source = readFileSync(file, 'utf8')
    const enums = [...source.matchAll(/^export enum (\w+)/gm)].map(m => m[1]!)
    expect(enums.length).toBeGreaterThan(0)
    const exported = entry as Record<string, unknown>
    for (const name of enums) expect(typeof exported[name], name).toBe('object')
  })

  it('枚举成员可直接当值用,数值与后端一致', () => {
    const skip: number = DuplicateStrategy.Skip
    expect(skip).toBe(0)
    expect(JobStatus.Ready).toBe(1)
  })
})
