#!/usr/bin/env node
// 生成 Phosphor 图标子集(src/assets/icons/ph-subset.json)。
//
// 为什么要子集:整套 ph 有 9000+ 个图标(4.5 MB / 946 KB gz),而全站静态代码只用到几十个。
// 启动时只同步 addCollection 这份子集,避免预热整集让每个用户首屏白拉掉 dist 四成的体积;
// 子集里没有的名字(消费者在菜单管理里配的图标)再由 AppIcon 懒加载整集兜底。
//
// 用法:
//   node scripts/gen-icon-subset.mjs            重新生成
//   node scripts/gen-icon-subset.mjs --check    只比对不写盘(单测/CI 用)
//   附加 --src=<目录> / --out=<文件> 可改扫描根与产物路径(测试用)
//
// 退出码非 0 的两种情况:图标名在 ph 里不存在(拼错),或 --check 下产物已过期。

import { readFile, readdir, writeFile, mkdir } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { getIcons } from '@iconify/utils'

const require = createRequire(import.meta.url)
const WEB_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

export const PREFIX = 'ph'
export const DEFAULT_SRC = path.join(WEB_ROOT, 'src')
export const DEFAULT_OUT = path.join(WEB_ROOT, 'src/assets/icons/ph-subset.json')

// 只扫这两类源文件;*.spec.ts 排除在外,免得测试里的假图标名混进产物。
const SCAN_EXT = new Set(['.vue', '.ts'])
const ICON_RE = /\bph:[a-z0-9]+(?:-[a-z0-9]+)*\b/g

/**
 * 内核种子菜单(后端 DefaultMenuSeed)配的图标。它们不出现在包的 src 里,却是登录后整条侧栏都在用的那批,
 * 漏掉就等于每个装机即用的用户仍要为侧栏把整集拉回来 —— 子集化白做。
 * 包的构建与单测不读 backend/,所以在这里显式列一份。后端新增图标而这里漏登记,
 * 只是退化成懒加载兜底,不会渲染不出来。
 */
export const SEED_ICONS = [
  'ph:bell-duotone',
  'ph:book-open-text-duotone',
  'ph:broadcast-duotone',
  'ph:buildings-duotone',
  'ph:clipboard-text-duotone',
  'ph:clock-countdown-duotone',
  'ph:database-duotone',
  'ph:files-duotone',
  'ph:folder-duotone',
  'ph:gauge-duotone',
  'ph:identification-badge-duotone',
  'ph:list-checks-duotone',
  'ph:list-dashes-duotone',
  'ph:pulse-duotone',
  'ph:qr-code-duotone',
  'ph:scroll-duotone',
  'ph:shield-check-duotone',
  'ph:sign-in-duotone',
  'ph:sliders-horizontal-duotone',
  'ph:squares-four-duotone',
  'ph:timer-duotone',
  'ph:trash-duotone',
  'ph:tree-structure-duotone',
  'ph:users-duotone',
  'ph:warning-octagon-duotone',
  'ph:wrench-duotone',
]

/** 递归扫描目录,收集全部 `ph:xxx` 引用(含种子清单),去重后升序返回。 */
export async function scanIconNames(srcDir = DEFAULT_SRC) {
  const found = new Set(SEED_ICONS)
  async function walk(dir) {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name)
      if (entry.isDirectory()) {
        await walk(full)
        continue
      }
      if (!SCAN_EXT.has(path.extname(entry.name)) || entry.name.endsWith('.spec.ts')) continue
      const text = await readFile(full, 'utf8')
      for (const m of text.matchAll(ICON_RE)) found.add(m[0])
    }
  }
  await walk(srcDir)
  return [...found].toSorted()
}

/** 读取完整的 @iconify-json/ph 图标集。 */
export async function loadFullSet() {
  return JSON.parse(await readFile(require.resolve('@iconify-json/ph/icons.json'), 'utf8'))
}

/** 按名字裁出子集;`missing` 是 ph 里查无此名的(基本都是拼错)。 */
export function buildSubset(full, names) {
  const bare = names.map(n => (n.startsWith(`${PREFIX}:`) ? n.slice(PREFIX.length + 1) : n))
  const subset = getIcons(full, bare, true) ?? { prefix: PREFIX, icons: {}, not_found: bare }
  const missing = (subset.not_found ?? []).map(n => `${PREFIX}:${n}`)
  delete subset.not_found
  return { subset, missing }
}

/** 与写盘一致的序列化(--check 靠逐字节比对)。 */
export function serialize(subset) {
  return `${JSON.stringify(subset, null, 2)}\n`
}

async function main() {
  const args = process.argv.slice(2)
  const check = args.includes('--check')
  const arg = flag => args.find(a => a.startsWith(`${flag}=`))?.slice(flag.length + 1)
  const srcDir = arg('--src') ?? DEFAULT_SRC
  const outFile = arg('--out') ?? DEFAULT_OUT

  const names = await scanIconNames(srcDir)
  const { subset, missing } = buildSubset(await loadFullSet(), names)
  if (missing.length) {
    console.error(`图标名在 Phosphor 里不存在(拼错?):\n  ${missing.join('\n  ')}`)
    process.exit(1)
  }

  const text = serialize(subset)
  const count = Object.keys(subset.icons ?? {}).length
  if (check) {
    const current = await readFile(outFile, 'utf8').catch(() => '')
    if (current !== text) {
      console.error(`${path.relative(WEB_ROOT, outFile)} 已过期,请跑 npm run gen:icons`)
      process.exit(1)
    }
    console.log(`图标子集已是最新(${count} 个)`)
    return
  }

  await mkdir(path.dirname(outFile), { recursive: true })
  await writeFile(outFile, text, 'utf8')
  console.log(`已写出 ${path.relative(WEB_ROOT, outFile)}(${count} 个图标)`)
}

// 被 import 时只取纯函数,不执行主流程。
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main()
}
