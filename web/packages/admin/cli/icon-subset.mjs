// Phosphor 图标子集:扫描源码里的 ph:* 名字,从 @iconify-json/ph 整集裁出用到的那几个。
// 内核的 scripts/gen-icon-subset.mjs 与随包发布的 smart-admin-icons 命令共用这一份,本模块没有副作用。
//
// 为什么要子集:整套 ph 有 9000+ 个图标(4.5 MB / 946 KB gz),一个应用的静态代码只用到几十个。
// 启动时同步注册子集,首帧即可离线渲染;子集外的名字(菜单管理里配的图标)由 AppIcon 懒加载整集兜底。
//
// 裁剪不引 @iconify/utils:这里只用得到它的 getIcons,它却会给每个消费方多装三个传递依赖。
// buildSubset 与 getIcons(data, names, true) 的产物逐字节一致,由 src/lib/iconSubset.spec.ts 拿 getIcons 对照锁住。

import { readFile, readdir, writeFile, mkdir } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'

const require = createRequire(import.meta.url)

export const PREFIX = 'ph'

const SCAN_EXT = new Set(['.vue', '.ts', '.tsx', '.js', '.jsx', '.mjs'])
// 测试文件里的假图标名不进产物
const TEST_FILE = /\.(spec|test)\.[^.]+$/
const ICON_RE = /\bph:[a-z0-9]+(?:-[a-z0-9]+)*\b/g

// 整集级的尺寸与 provider 字段,裁出的子集原样带上(同 getIcons 的 propsToCopy)
const ROOT_PROPS = ['left', 'top', 'width', 'height', 'provider']

/** 递归扫描目录,收集全部 `ph:xxx` 引用并入 `extra`,去重后升序返回。跳过 node_modules 与 . 开头的目录。 */
export async function scanIconNames(srcDir, extra = []) {
  const found = new Set(extra)
  async function walk(dir) {
    for (const entry of await readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name)
      if (entry.isDirectory()) {
        if (entry.name !== 'node_modules' && !entry.name.startsWith('.')) await walk(full)
        continue
      }
      if (!SCAN_EXT.has(path.extname(entry.name)) || TEST_FILE.test(entry.name)) continue
      const text = await readFile(full, 'utf8')
      for (const m of text.matchAll(ICON_RE)) found.add(m[0])
    }
  }
  await walk(srcDir)
  return [...found].toSorted()
}

/** 读取完整的 @iconify-json/ph 图标集。它是本包的依赖,从本模块所在位置解析,消费方不必自己装。 */
export async function loadFullSet() {
  return JSON.parse(await readFile(require.resolve('@iconify-json/ph/icons.json'), 'utf8'))
}

/**
 * 按名字裁出子集;`missing` 是 ph 里查无此名的(基本都是拼错)。别名沿 parent 链追到真图标,
 * 链上每一环都进子集。结果的键序、别名处理与 getIcons 相同:prefix、icons、lastModified、aliases,最后是整集级字段。
 */
export function buildSubset(full, names) {
  const bare = names.map(n => (n.startsWith(`${PREFIX}:`) ? n.slice(PREFIX.length + 1) : n))
  const sourceIcons = full.icons
  const sourceAliases = full.aliases || Object.create(null)

  // 名字 → 追到真图标所经的 parent 链([] = 本身就是图标,null = 查无此名或链断了)
  const resolved = Object.create(null)
  const resolve = name => {
    if (sourceIcons[name]) return (resolved[name] = [])
    if (!(name in resolved)) {
      resolved[name] = null
      const parent = sourceAliases[name] && sourceAliases[name].parent
      const chain = parent && resolve(parent)
      if (chain) resolved[name] = [parent, ...chain]
    }
    return resolved[name]
  }
  bare.forEach(resolve)

  const subset = { prefix: full.prefix, icons: Object.create(null) }
  if (full.lastModified) subset.lastModified = full.lastModified
  const missing = []
  for (const name in resolved) {
    if (!resolved[name]) {
      if (bare.includes(name)) missing.push(`${PREFIX}:${name}`)
    } else if (sourceIcons[name]) {
      subset.icons[name] = { ...sourceIcons[name] }
    } else {
      subset.aliases ??= Object.create(null)
      subset.aliases[name] = { ...sourceAliases[name] }
    }
  }
  for (const key of ROOT_PROPS) if (key in full) subset[key] = full[key]
  return { subset, missing }
}

/** 与写盘一致的序列化(--check 靠逐字节比对)。 */
export function serialize(subset) {
  return `${JSON.stringify(subset, null, 2)}\n`
}

/**
 * 扫描 `src`、裁出子集后写进 `out`;`check` 为真时只比对不写盘。`extra` 是源码里没有、也要进子集的名字,
 * `staleHint` 接在「已过期」提示后面告诉人怎么重生成。返回进程退出码:0 成功,1 有拼错的名字或产物已过期。
 */
export async function generate({ src, out, check = false, extra = [], staleHint }) {
  let names
  try {
    names = await scanIconNames(src, extra)
  } catch (e) {
    if (e?.code !== 'ENOENT') throw e
    console.error(`扫描目录不存在:${src}`)
    return 1
  }

  const { subset, missing } = buildSubset(await loadFullSet(), names)
  if (missing.length) {
    console.error(`图标名在 Phosphor 里不存在(拼错?):\n  ${missing.join('\n  ')}`)
    return 1
  }

  const text = serialize(subset)
  const count = Object.keys(subset.icons).length
  const shown = path.relative(process.cwd(), out) || out
  if (check) {
    const current = await readFile(out, 'utf8').catch(() => '')
    if (current !== text) {
      console.error(`${shown} 已过期,${staleHint}`)
      return 1
    }
    console.log(`图标子集已是最新(${count} 个)`)
    return 0
  }

  await mkdir(path.dirname(out), { recursive: true })
  await writeFile(out, text, 'utf8')
  console.log(`已写出 ${shown}(${count} 个图标)`)
  return 0
}
