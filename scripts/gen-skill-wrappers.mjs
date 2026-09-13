#!/usr/bin/env node
// 把 .claude/skills/<name>/SKILL.md 镜像到 .agents/ 和 .codex/。
//
// 为什么要镜像:skills/*.md 是单一真源,但三个 agent 运行时各自只认自己那个目录下的
// SKILL.md 入口。手工维护三份的结果已经见过一次 —— 11 个 skill 里只有 1 个被包装到
// .agents/.codex,另外 10 个对 Codex 和别的 agent 完全不可见。
//
//   node scripts/gen-skill-wrappers.mjs           写入(缺的建、变的改、多的删)
//   node scripts/gen-skill-wrappers.mjs --check   只比对,不一致时退出码 1
//
// --check 用在「改完 .claude/skills/ 忘了同步」的场合。它不判包装内容对不对,
// 只判三份一不一致 —— 内容对不对由人看,一致不一致机器能判。
//
// 镜像不是逐字节:frontmatter 只保留 Agent Skills 规范认的字段。disable-model-invocation、
// argument-hint 这些是 Claude Code 私有的,别的运行时遇到陌生键未必宽容,所以只留在 .claude 侧。
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const SOURCE = path.join(repo, '.claude/skills')
const TARGETS = ['.agents/skills', '.codex/skills'].map(t => path.join(repo, t))
const SKILLS_DIR = path.join(repo, 'skills')

const check = process.argv.includes('--check')
const problems = []
let changed = 0

function read(file) {
  return fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null
}

const PORTABLE = new Set(['name', 'description', 'license', 'compatibility', 'metadata', 'allowed-tools'])

// 去掉 frontmatter 里 Claude Code 私有的字段;缩进的续行(YAML 列表项)跟着它上面的键一起去留
function portable(body) {
  const m = body.match(/^---\n([\s\S]*?)\n---\n/)
  if (!m) return body
  let keep = true
  const lines = m[1].split('\n').filter(line => {
    const key = /^([\w-]+):/.exec(line)?.[1]
    if (key !== undefined) keep = PORTABLE.has(key)
    return keep
  })
  return `---\n${lines.join('\n')}\n---\n${body.slice(m[0].length)}`
}

if (!fs.existsSync(SOURCE)) {
  console.error(`找不到包装源目录 ${path.relative(repo, SOURCE)}`)
  process.exit(1)
}

const names = fs
  .readdirSync(SOURCE, { withFileTypes: true })
  .filter(e => e.isDirectory())
  .map(e => e.name)
  .sort()

if (names.length === 0) {
  console.error(`${path.relative(repo, SOURCE)} 下一个 skill 都没有,不像是对的`)
  process.exit(1)
}

for (const name of names) {
  const src = path.join(SOURCE, name, 'SKILL.md')
  const body = read(src)
  if (body === null) {
    problems.push(`${name}: 缺 .claude/skills/${name}/SKILL.md`)
    continue
  }
  // 包装指向的真源必须真的存在,否则镜像出去的是三份坏链接
  if (!fs.existsSync(path.join(SKILLS_DIR, `${name}.md`))) {
    problems.push(`${name}: 包装存在,但 skills/${name}.md 不在 —— 真源被删了还是改名了?`)
  }
  const mirror = portable(body)
  for (const target of TARGETS) {
    const dst = path.join(target, name, 'SKILL.md')
    if (read(dst) === mirror) continue
    changed++
    const rel = path.relative(repo, dst).split(path.sep).join('/')
    if (check) {
      problems.push(`${rel} 与 .claude 侧不一致`)
    } else {
      fs.mkdirSync(path.dirname(dst), { recursive: true })
      fs.writeFileSync(dst, mirror)
      console.log(`写入 ${rel}`)
    }
  }
}

// 真源没了的包装要清掉:留着就是给 agent 指一条死路
for (const target of TARGETS) {
  if (!fs.existsSync(target)) continue
  for (const e of fs.readdirSync(target, { withFileTypes: true })) {
    if (!e.isDirectory() || names.includes(e.name)) continue
    const rel = path.relative(repo, path.join(target, e.name)).split(path.sep).join('/')
    changed++
    if (check) problems.push(`${rel} 是多余的(.claude 侧没有同名 skill)`)
    else {
      fs.rmSync(path.join(target, e.name), { recursive: true, force: true })
      console.log(`删除 ${rel}`)
    }
  }
}

if (problems.length) {
  console.error('\n包装不同步:')
  for (const p of problems) console.error(`  - ${p}`)
  console.error('\n跑 `node scripts/gen-skill-wrappers.mjs` 修好,然后把改动一起提交。')
  process.exit(1)
}

console.log(
  check
    ? `${names.length} 个 skill 的包装三份一致。`
    : changed === 0
      ? `${names.length} 个 skill 的包装本来就是一致的,没动。`
      : `${names.length} 个 skill,同步了 ${changed} 处。`,
)
