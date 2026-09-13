/**
 * E2E 端口协调:探测 + 跨进程锁文件(pid),减少并行 run 争用。
 * CI=true 时必须由工作流注入 SMART_E2E_API_PORT / SMART_E2E_WEB_PORT,缺失直接失败。
 */
import { createServer } from 'node:net'
import {
  openSync,
  writeSync,
  closeSync,
  unlinkSync,
  existsSync,
  readFileSync,
} from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

function isPidAlive(pid) {
  if (!Number.isFinite(pid) || pid <= 0) return false
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

function tryClaimLock(port) {
  const lockPath = join(tmpdir(), `smart-e2e-port-${port}.lock`)
  try {
    if (existsSync(lockPath)) {
      const raw = readFileSync(lockPath, 'utf8')
      const pid = parseInt(String(raw).split(/\r?\n/)[0], 10)
      if (isPidAlive(pid)) return null
      try {
        unlinkSync(lockPath)
      } catch {
        /* stale */
      }
    }
    const fd = openSync(lockPath, 'wx')
    writeSync(fd, `${process.pid}\n${Date.now()}\n`)
    closeSync(fd)
    const release = () => {
      try {
        unlinkSync(lockPath)
      } catch {
        /* ignore */
      }
    }
    process.once('exit', release)
    return { port, release, lockPath }
  } catch {
    return null
  }
}

function tryBind(port) {
  return new Promise((resolve) => {
    const s = createServer()
    s.unref()
    s.once('error', () => resolve(false))
    s.listen(port, '127.0.0.1', () => {
      s.close((err) => resolve(!err))
    })
  })
}

async function findAndClaim(min, span) {
  const start = min + Math.floor(Math.random() * Math.max(1, span - 50))
  for (let i = 0; i < span; i++) {
    const p = min + ((start - min + i) % span)
    if (!(await tryBind(p))) continue
    const claim = tryClaimLock(p)
    if (claim) return claim
  }
  throw new Error(`no free+claimable port in [${min}, ${min + span})`)
}

/**
 * @param {{ apiMin: number, webMin: number, span?: number }} ranges
 */
export async function resolvePortPair(ranges) {
  const span = ranges.span ?? 4000
  const ci = process.env.CI === 'true' || process.env.SMART_E2E_REQUIRE_PORTS === '1'
  const existingApi = Number(process.env.SMART_E2E_API_PORT)
  const existingWeb = Number(process.env.SMART_E2E_WEB_PORT)

  if (ci) {
    if (!(Number.isFinite(existingApi) && existingApi > 0 && Number.isFinite(existingWeb) && existingWeb > 0)) {
      throw new Error(
        'CI e2e requires explicit SMART_E2E_API_PORT and SMART_E2E_WEB_PORT (unique per job matrix).',
      )
    }
    if (existingApi === existingWeb) {
      throw new Error('SMART_E2E_API_PORT and SMART_E2E_WEB_PORT must differ')
    }
    // 端口锁是给「同机多个本地 e2e 并发」防撞用的,而 CI 上端口由 workflow 逐 job 显式指定,
    // 唯一性由那边保证。这里不能因为锁被占就报错:playwright 的每个 worker 都是独立进程,
    // 都会重新加载这个配置——第一个(起 webServer 的那个)拿到锁,其余五个必然拿不到,
    // 于是每条用例开跑前先自杀。占不到就当没有锁,端口照用。
    const apiClaim = tryClaimLock(existingApi) ?? { port: existingApi, release: () => {} }
    const webClaim = tryClaimLock(existingWeb) ?? { port: existingWeb, release: () => {} }
    return { apiPort: existingApi, webPort: existingWeb, release: () => { apiClaim.release(); webClaim.release() } }
  }

  if (Number.isFinite(existingApi) && existingApi > 0 && Number.isFinite(existingWeb) && existingWeb > 0) {
    if (existingApi === existingWeb) throw new Error('SMART_E2E_API_PORT and SMART_E2E_WEB_PORT must differ')
    const apiClaim = tryClaimLock(existingApi) ?? { port: existingApi, release: () => {} }
    const webClaim = tryClaimLock(existingWeb) ?? { port: existingWeb, release: () => {} }
    process.env.SMART_E2E_API_PORT = String(existingApi)
    process.env.SMART_E2E_WEB_PORT = String(existingWeb)
    return {
      apiPort: existingApi,
      webPort: existingWeb,
      release: () => {
        apiClaim.release()
        webClaim.release()
      },
    }
  }

  const apiClaim = await findAndClaim(ranges.apiMin, span)
  const webClaim = await findAndClaim(ranges.webMin, span)
  process.env.SMART_E2E_API_PORT = String(apiClaim.port)
  process.env.SMART_E2E_WEB_PORT = String(webClaim.port)
  return {
    apiPort: apiClaim.port,
    webPort: webClaim.port,
    release: () => {
      apiClaim.release()
      webClaim.release()
    },
  }
}
