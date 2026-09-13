// llms.txt 是给模型读的站点索引。它有两种坏法，都不会有任何东西报错：
//   1. 指向已被删掉或改名的页面 —— 模型抓到 404；
//   2. 站点加了新页而索引没跟上 —— 模型根本不知道那页存在。
// 所以这里两头都钉。
import { readFileSync, readdirSync, statSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const SITE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const ORIGIN = 'https://smartcode-x.github.io/SmartAdmin'

/** 索引里不必出现的页：站点骨架与自动生成页。 */
const EXEMPT = new Set(['changelog.md', 'zh/changelog.md', '404.md'])

/** 收集站点里所有 md 页面，返回站点根下的相对路径。 */
function pages(dir = SITE, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === '.vitepress' || name === 'public' || name === 'dist') continue
    const full = path.join(dir, name)
    if (statSync(full).isDirectory()) pages(full, out)
    else if (name.endsWith('.md')) out.push(path.relative(SITE, full).split(path.sep).join('/'))
  }
  return out
}

/** 把 llms.txt 里的一条 URL 还原成它指向的页面文件。 */
function toPage(url) {
  const rel = url.replace(/^\//, '')
  for (const c of [`${rel}.md`, `${rel}index.md`, `${rel.replace(/\/$/, '')}/index.md`, rel === '' ? 'index.md' : null]) {
    if (c && all.includes(c)) return c
  }
  return null
}

const all = pages()
const txt = readFileSync(path.join(SITE, 'public/llms.txt'), 'utf8')
// 不用正则：这里只需要「原点之后、右括号之前」那一段，正则的转义在跨平台脚本里更容易写错。
const urls = txt.split(ORIGIN).slice(1).map(s => s.slice(0, s.indexOf(')')))

const dead = urls.filter(u => !toPage(u))
const listed = new Set(urls.map(toPage).filter(Boolean))
const missing = all.filter(p => !listed.has(p) && !EXEMPT.has(p))

if (dead.length || missing.length) {
  for (const u of dead) console.error(`死链：llms.txt 指向 ${u}，站点里没有对应页面`)
  for (const p of missing) console.error(`漏登记：${p} 没有出现在 llms.txt 里`)
  console.error(`\nllms.txt 与站点不同步：${dead.length} 条死链，${missing.length} 页未登记。`)
  process.exit(1)
}

console.log(`llms.txt 与站点同步：${urls.length} 条链接，覆盖 ${all.length - EXEMPT.size} 个页面。`)
