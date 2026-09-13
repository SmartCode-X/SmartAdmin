#!/usr/bin/env node
// 生成 src/api/schema.d.ts:向运行中的后端拉 /openapi/v1.json,交给 openapi-typescript 转成类型。
//
// 为什么不把 openapi-typescript 那行直接写在 npm script 里:
// 后端端口不是常量。dev 代理用 SMART_API_TARGET 指到别处(见 vite.config.ts),gen:api 若写死
// http://localhost:5100,后端跑在别的端口时这条命令要么连不上,要么把「另一个后端」的契约
// 静默写进 schema.d.ts。npm script 里没有跨平台的环境变量展开写法($VAR 在 cmd.exe 不展开),
// 所以走一个 node 脚本,让两处读同一个变量。
//
// 用法:
//   npm run gen:api                                          默认 http://localhost:5100
//   SMART_API_TARGET=http://localhost:5200 npm run gen:api    与 dev 代理同一个变量
//   npm run gen:api -- --target http://127.0.0.1:5200         显式指定,优先级最高
import { spawnSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { dirname, resolve as resolvePath } from 'node:path'
import { fileURLToPath } from 'node:url'

const argv = process.argv.slice(2)
const flagIndex = argv.findIndex((a) => a === '--target' || a.startsWith('--target='))
const flagTarget =
  flagIndex === -1
    ? undefined
    : argv[flagIndex].includes('=')
      ? argv[flagIndex].split('=').slice(1).join('=')
      : argv[flagIndex + 1]

// 与 vite.config.ts 的 apiTarget 同一个变量、同一个默认值(5100 而非 5000:macOS 的 AirPlay 占了 5000)。
// 空串按「没设」处理:`SMART_API_TARGET=` 用 `??` 会落在空串上,拼出来的 URL 变成裸 `/openapi/v1.json`,
// 报错还看不出是哪来的。
const explicit = [flagTarget, process.env.SMART_API_TARGET].find((v) => typeof v === 'string' && v.trim() !== '')
const target = (explicit ?? 'http://localhost:5100').trim().replace(/\/+$/, '')

let url
try {
  url = new URL('/openapi/v1.json', target).href
} catch {
  console.error(`[gen:api] 目标地址不合法:${target}(应形如 http://localhost:5100)`)
  process.exit(1)
}

const webRoot = resolvePath(dirname(fileURLToPath(import.meta.url)), '..')
const out = resolvePath(webRoot, 'src/api/schema.d.ts')

// 包的 exports 把 "./*.js" 映射到 "./*.mjs",所以 require.resolve('openapi-typescript/bin/cli.js') 解不出来;
// 先解 package.json 再按它自己声明的 bin 拼路径,不写死 node_modules 布局(pnpm/yarn 的目录结构不同)。
const require = createRequire(import.meta.url)
const pkgPath = require.resolve('openapi-typescript/package.json')
const cli = resolvePath(dirname(pkgPath), require(pkgPath).bin['openapi-typescript'])

console.log(`[gen:api] ${url} -> src/api/schema.d.ts`)
const result = spawnSync(process.execPath, [cli, url, '-o', out], { stdio: 'inherit', cwd: webRoot })

if (result.error) {
  console.error(`[gen:api] 启动 openapi-typescript 失败:${result.error.message}`)
  process.exit(1)
}
if (result.status !== 0) {
  console.error(
    `[gen:api] 生成失败。后端跑起来了吗?/openapi 只在 Development 环境挂载;` +
      `后端不在 ${target} 时用 SMART_API_TARGET 或 --target 指过去。`,
  )
  process.exit(result.status ?? 1)
}
