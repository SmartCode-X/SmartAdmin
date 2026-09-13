#!/usr/bin/env node
// smart-admin-icons:给应用生成自己的 ph 离线图标子集,交给 createSmartAdmin({ iconSets }) 启动时同步注册。
// 扫描、裁剪、--check 的逻辑在 icon-subset.mjs,本文件只解析参数。退出码:0 成功;1 图标名拼错或 --check 下产物过期;2 参数错误。
//
// 不加「被 import 时不执行」的守卫:npm 在 POSIX 上把 node_modules/.bin 里的命令做成符号链接,
// process.argv[1] 是链接路径而不是本文件,拿它和 import.meta.url 比对,命令会静默什么都不做。

import path from 'node:path'
import { generate } from './icon-subset.mjs'

const USAGE = `用法:smart-admin-icons [--src <目录>] [--out <文件>] [--check]

扫描 --src 下 .vue/.ts/.tsx/.js/.jsx/.mjs 里的 ph:* 图标名(跳过 *.spec.*、*.test.*、node_modules),
从 Phosphor 整集裁出这些图标写进 --out,交给 createSmartAdmin({ iconSets }) 启动时同步注册。
源码里没有、却要离线可用的名字(比如种子菜单的图标),写进 --src 下任意一个 .ts 就会被扫进来。

  --src <目录>   扫描根,默认 src
  --out <文件>   产物路径,默认 src/assets/icons/ph-subset.json
  --check        只比对不写盘:产物过期或有拼错的名字时非 0 退出(CI 用)
  --help         显示本说明`

function parseArgs(argv) {
  const opts = { src: 'src', out: 'src/assets/icons/ph-subset.json', check: false, help: false }
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === '--check') opts.check = true
    else if (arg === '--help' || arg === '-h') opts.help = true
    else {
      // --src x 与 --src=x 两种写法都认
      const m = /^--(src|out)(?:=(.*))?$/.exec(arg)
      if (!m) throw new Error(`未知参数:${arg}`)
      const value = m[2] ?? argv[++i]
      if (!value || value.startsWith('--')) throw new Error(`--${m[1]} 缺少值`)
      opts[m[1]] = value
    }
  }
  return opts
}

let opts
try {
  opts = parseArgs(process.argv.slice(2))
} catch (e) {
  console.error(`${e.message}\n\n${USAGE}`)
  process.exitCode = 2
}

if (opts?.help) {
  console.log(USAGE)
} else if (opts) {
  process.exitCode = await generate({
    src: path.resolve(opts.src),
    out: path.resolve(opts.out),
    check: opts.check,
    staleHint: '去掉 --check 再跑一次即可更新',
  })
}
