/// <reference types="node" />
// 随包发布的 smart-admin-icons 与它的共享模块 cli/icon-subset.mjs。要真的起进程看退出码,故显式引 node 类型。
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { getIcons } from '@iconify/utils'
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import kernelSubset from '#/assets/icons/ph-subset.json'

const cliDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../cli')
const bin = path.join(cliDir, 'smart-admin-icons.mjs')

type IconSet = Parameters<typeof getIcons>[0]
interface IconSubsetModule {
  buildSubset(full: IconSet, names: string[]): { subset: IconSet; missing: string[] }
  serialize(subset: IconSet): string
}

// cli 下是不带类型声明的纯 JS,按运行时路径动态导入
let mod: IconSubsetModule
let full: IconSet
beforeAll(async () => {
  mod = await import(pathToFileURL(path.join(cliDir, 'icon-subset.mjs')).href)
  const require = createRequire(import.meta.url)
  full = JSON.parse(readFileSync(require.resolve('@iconify-json/ph/icons.json'), 'utf8'))
})

// 自实现的裁剪必须与 @iconify/utils 的 getIcons 逐字节一致:它是 Iconify 官方的参照实现,
// 不引它只是为了不给消费方多装传递依赖,语义上不许有任何偏差。
const reference = (set: IconSet, names: string[]) => {
  const out = getIcons(
    set,
    names.map(n => n.replace(/^ph:/, '')),
    true,
  )!
  const missing = (out.not_found ?? []).map(n => `ph:${n}`)
  delete out.not_found
  return { text: mod.serialize(out), missing }
}
const ours = (set: IconSet, names: string[]) => {
  const { subset, missing } = mod.buildSubset(set, names)
  return { text: mod.serialize(subset), missing }
}

describe('buildSubset 与 getIcons 对照', () => {
  it('内核子集的全部名字:产物相同,且与已提交的内核子集逐字节一致', () => {
    const names = Object.keys(kernelSubset.icons).map(n => `ph:${n}`)
    expect(ours(full, names)).toEqual(reference(full, names))
    const committed = readFileSync(
      path.resolve(cliDir, '../src/assets/icons/ph-subset.json'),
      'utf8',
    )
    expect(ours(full, names).text).toBe(committed)
  })

  it('ph 集的全部别名:别名与它追到的真图标一起进子集', () => {
    const aliases = Object.keys(full.aliases ?? {}).map(n => `ph:${n}`)
    expect(aliases.length).toBeGreaterThan(0)
    expect(ours(full, aliases)).toEqual(reference(full, aliases))
    expect(Object.keys(mod.buildSubset(full, aliases).subset.aliases ?? {})).toHaveLength(
      aliases.length,
    )
  })

  it('别名链与断链:逐环进子集,断链与查无此名都算缺失', () => {
    // ph 里没有别名套别名,造一份带链的集
    const chained: IconSet = {
      prefix: 'ph',
      lastModified: 1,
      icons: { base: { body: '<g/>' } },
      aliases: {
        mid: { parent: 'base', rotate: 1 },
        top: { parent: 'mid', hFlip: true },
        broken: { parent: 'gone' },
      },
      width: 24,
      height: 24,
    }
    const names = ['ph:top', 'ph:broken', 'ph:nope']
    expect(ours(chained, names)).toEqual(reference(chained, names))
    expect(ours(chained, names).missing).toEqual(['ph:broken', 'ph:nope'])
  })

  it('混入拼错的名字:缺失清单相同', () => {
    const names = ['ph:bell', 'ph:definitely-not-an-icon', 'ph:activity-duotone', 'ph:nope']
    const result = ours(full, names)
    expect(result).toEqual(reference(full, names))
    expect(result.missing).toEqual(['ph:definitely-not-an-icon', 'ph:nope'])
  })

  it('空名单', () => {
    expect(ours(full, [])).toEqual(reference(full, []))
  })
})

describe('smart-admin-icons 命令', () => {
  let dir: string
  const file = (rel: string) => path.join(dir, rel)
  const write = (rel: string, text: string) => {
    mkdirSync(path.dirname(file(rel)), { recursive: true })
    writeFileSync(file(rel), text)
  }
  const run = (args: string[] = []) =>
    spawnSync(process.execPath, [bin, ...args], { cwd: dir, encoding: 'utf8' })
  const iconsIn = (rel: string) =>
    Object.keys(JSON.parse(readFileSync(file(rel), 'utf8')).icons as object)

  beforeEach(() => {
    dir = mkdtempSync(path.join(tmpdir(), 'smart-admin-icons-'))
    write('src/views/a.vue', '<template><AppIcon icon="ph:factory-duotone" /></template>')
    write('src/widgets/b.tsx', 'export const B = () => <AppIcon icon="ph:anchor" />')
    // 源码里没有的名字(种子菜单用到的)写进任意 .ts 就会被扫进来
    write('src/menu-icons.ts', "export const MENU_ICONS = ['ph:truck-duotone']")
    // 测试文件、依赖目录、. 开头的目录都不扫
    write('src/views/a.spec.ts', "const x = 'ph:bell'")
    write('src/utils/c.test.js', "const x = 'ph:acorn'")
    write('src/node_modules/x/index.js', "const x = 'ph:airplane'")
    write('src/.cache/d.ts', "const x = 'ph:alarm'")
  })
  afterEach(() => rmSync(dir, { recursive: true, force: true }))

  it('不带参数:扫 cwd 下的 src,写到 src/assets/icons/ph-subset.json', () => {
    const r = run()
    expect(r.status, r.stderr).toBe(0)
    expect(iconsIn('src/assets/icons/ph-subset.json')).toEqual([
      'anchor',
      'factory-duotone',
      'truck-duotone',
    ])
  })

  it('--check:新鲜时退出 0;源码多了图标就报过期且不写盘', () => {
    expect(run().status).toBe(0)
    const fresh = run(['--check'])
    expect(fresh.status, fresh.stderr).toBe(0)
    expect(fresh.stdout).toContain('已是最新')

    const before = readFileSync(file('src/assets/icons/ph-subset.json'), 'utf8')
    write('src/views/e.vue', '<template><AppIcon icon="ph:bell" /></template>')
    const stale = run(['--check'])
    expect(stale.status).toBe(1)
    expect(stale.stderr).toContain('已过期')
    expect(readFileSync(file('src/assets/icons/ph-subset.json'), 'utf8')).toBe(before)
  })

  it('拼错的图标名:退出 1,列出名字,不写产物', () => {
    write('src/views/typo.vue', '<template><AppIcon icon="ph:definitely-not-an-icon" /></template>')
    const r = run()
    expect(r.status).toBe(1)
    expect(r.stderr).toContain('ph:definitely-not-an-icon')
    expect(existsSync(file('src/assets/icons/ph-subset.json'))).toBe(false)
  })

  it('--src x 与 --src=x 两种写法产物相同', () => {
    expect(run(['--src', 'src', '--out', 'out/a.json']).status).toBe(0)
    expect(run(['--src=src', '--out=out/b.json']).status).toBe(0)
    expect(readFileSync(file('out/a.json'), 'utf8')).toBe(readFileSync(file('out/b.json'), 'utf8'))
    expect(iconsIn('out/a.json')).toEqual(['anchor', 'factory-duotone', 'truck-duotone'])
  })

  it('--help 退出 0;未知参数与缺值退出 2;扫描目录不存在退出 1', () => {
    const help = run(['--help'])
    expect(help.status).toBe(0)
    expect(help.stdout).toContain('用法')
    expect(run(['--bogus']).status).toBe(2)
    expect(run(['--src']).status).toBe(2)
    const missing = run(['--src', 'no-such-dir'])
    expect(missing.status).toBe(1)
    expect(missing.stderr).toContain('扫描目录不存在')
  })
})
