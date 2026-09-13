#!/usr/bin/env node
// 生成内核自己的 Phosphor 图标子集(src/assets/icons/ph-subset.json),启动时由 lib/icons.ts 同步注册。
// 扫描、裁剪、--check 与随包发布的 smart-admin-icons 命令是同一份逻辑(cli/icon-subset.mjs),
// 这里只补内核特有的部分:包内的扫描根与产物路径,和不出现在 src 里的种子菜单图标。
//
// 用法:
//   node scripts/gen-icon-subset.mjs            重新生成
//   node scripts/gen-icon-subset.mjs --check    只比对不写盘(单测/CI 用)
//   附加 --src=<目录> / --out=<文件> 可改扫描根与产物路径(测试用)
//
// 退出码非 0 的两种情况:图标名在 ph 里不存在(拼错),或 --check 下产物已过期。

import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { generate } from '../cli/icon-subset.mjs'

const WEB_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

/**
 * 内核种子菜单(后端 DefaultMenuSeed)配的图标。它们不出现在包的 src 里,却是登录后整条侧栏都在用的那批,
 * 漏掉就等于每个装机即用的用户仍要为侧栏把整集拉回来 —— 子集化白做。
 * 包的构建与单测不读 backend/,所以在这里显式列一份。后端新增图标而这里漏登记,
 * 只是退化成懒加载兜底,不会渲染不出来。
 */
const SEED_ICONS = [
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

const args = process.argv.slice(2)
const arg = flag => args.find(a => a.startsWith(`${flag}=`))?.slice(flag.length + 1)

process.exitCode = await generate({
  src: arg('--src') ?? path.join(WEB_ROOT, 'src'),
  out: arg('--out') ?? path.join(WEB_ROOT, 'src/assets/icons/ph-subset.json'),
  check: args.includes('--check'),
  extra: SEED_ICONS,
  staleHint: '请跑 npm run gen:icons',
})
