/// <reference types="node" />
// 本用例要真的把生成脚本跑起来看退出码,故显式引 node 类型(tsconfig 的 types 只给 vite/client)。
import { execFileSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import subset from '#/assets/icons/ph-subset.json'

// 离线 ph 子集是构建产物,靠脚本的 --check 钉住它没过期 —— 过期的后果是静默的:
// 新加的图标在子集外,页面会退化成懒加载整集(4.5 MB),看着一切正常,只是首屏白胖了。
const script = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../../scripts/gen-icon-subset.mjs',
)

const run = (args: string[]) => execFileSync(process.execPath, [script, ...args], { stdio: 'pipe' })

describe('ph 图标子集', () => {
  it('产物与 src 里的图标用法一致(过期就跑 npm run gen:icons)', () => {
    expect(() => run(['--check'])).not.toThrow()
  })

  it('拼错的图标名让脚本非 0 退出', () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'smart-icons-'))
    try {
      mkdirSync(path.join(dir, 'views'))
      writeFileSync(
        path.join(dir, 'views/a.vue'),
        '<template><AppIcon icon="ph:definitely-not-an-icon" /></template>',
      )
      expect(() => run(['--check', `--src=${dir}`, `--out=${path.join(dir, 'o.json')}`])).toThrow()
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })

  it('是子集而非整集(整集 9000+,子集应在两三百以内)', () => {
    expect(subset.prefix).toBe('ph')
    const count = Object.keys(subset.icons).length
    expect(count).toBeGreaterThan(50)
    expect(count).toBeLessThan(300)
  })

  it('含 AppIcon 的空值兜底图标(缺了会全站显示空槽)', () => {
    expect(subset.icons).toHaveProperty('dot-outline-duotone')
    expect(subset.icons).toHaveProperty('folder-duotone')
  })
})
