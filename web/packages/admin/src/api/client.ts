import createClient, { type Client, type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '#/stores/user'
import { runtime } from '#/lib/runtime'
import { REAUTH_REQUIRED_CODE, REAUTH_RETRY_HEADER, requestReauth } from './reauthGate'

// 默认空 apiBase = 同源:schema 的 path 键已含 /api/v1,dev 下由 Vite proxy 反代到后端(默认 :5100),
// 生产下由 nginx 反代或后端自己托管 dist(见文档站部署指南的路线 A / B)。
// 只有前端与 API 真的不同源(CDN / 独立域名)时才需要 createSmartAdmin({ apiBase: 'https://api.example.com' }),
// 此时后端还必须配 SmartAdmin:Api:Cors:AllowedOrigins(默认 deny-all)。

/**
 * 请求超时:没有超时的话,后端卡死或链路半开时 fetch 会一直挂着 —— 页面永远转圈,
 * 按钮的 loading 永不结束,用户只能刷新。
 * 默认 30 秒;上传/分片/导出/导入提交这类天然慢的调用在 api 层显式传 `timeout: LONG_TIMEOUT_MS`。
 */
export const DEFAULT_TIMEOUT_MS = 30_000
export const LONG_TIMEOUT_MS = 10 * 60_000

/** 与后端 AuthCookieNames 对齐(禁硬编码纪律的前端镜像)。 */
const CSRF_COOKIE = 'smart_csrf'
const CSRF_HEADER = 'X-Smart-CSRF'

/**
 * 读 document.cookie 中的可读 Cookie(双提交 CSRF 的 smart_csrf)。
 * HttpOnly 的 smart_rt 读不到,只能靠 credentials 自动携带。
 */
export function readCookie(name: string): string {
  if (typeof document === 'undefined') return ''
  const parts = document.cookie.split(';')
  for (const part of parts) {
    const i = part.indexOf('=')
    if (i < 0) continue
    const k = part.slice(0, i).trim()
    if (k === name) return decodeURIComponent(part.slice(i + 1).trim())
  }
  return ''
}

/** 写请求是否需要附 CSRF(有可读 CSRF Cookie 即附;无 Cookie 时不附)。 */
function isMutating(method: string): boolean {
  const m = method.toUpperCase()
  return m !== 'GET' && m !== 'HEAD' && m !== 'OPTIONS'
}

function attachCsrf(headers: Headers) {
  const csrf = readCookie(CSRF_COOKIE)
  if (csrf) headers.set(CSRF_HEADER, csrf)
}

/**
 * 给每个请求装超时闸。必须**最先**注册:后面的重放中间件要克隆最终的 Request,顺序反了就克隆到旧的那份。
 *
 * 调用方传的 `signal` 依然生效 —— 这里用 `AbortSignal.any` 把它和超时信号并起来,谁先响算谁,
 * 不是把调用方的取消能力顶掉。单次调用要放宽时限就多传一个 `timeout`(毫秒),
 * openapi-fetch 会把它这类自定义键原样挂到 Request 上(见其 InitParam 的 `[key: string]: unknown`)。
 */
const timeoutMiddleware: Middleware = {
  onRequest({ request }) {
    const ms = (request as Request & { timeout?: unknown }).timeout
    const limit = typeof ms === 'number' && ms > 0 ? ms : DEFAULT_TIMEOUT_MS
    return new Request(request, {
      signal: AbortSignal.any([request.signal, AbortSignal.timeout(limit)]),
    })
  },
}

/** 请求前注入 Bearer + 写操作 CSRF。请求时读 store,始终拿最新令牌。 */
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(request.method)) attachCsrf(request.headers)
    return request
  },
}

// 令牌过期重放:Request 的 body 是一次性流,首次 fetch 就被消费,直接重放会丢 body
//(GET 无 body 不受影响,但令牌恰好在一次 POST/PUT 时过期就会丢请求体)。
// 发出前克隆一份副本(clone 会 tee body 流,与原请求各读各的),重放时用这份未消费的副本。
// 键是发出前的 Request 实例(openapi-fetch 把它一路带到 onResponse),WeakMap 随请求回收自动清理。
const replayable = new WeakMap<Request, Request>()

// 并发 401 合流到同一次刷新。
let refreshing: Promise<boolean> | null = null

/** 单飞静默刷新;路由守卫 F5 重建 Cookie 会话时也可调用。 */
export function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
}

/**
 * 确保有可用 accessToken:已有则 true;Cookie 会话或 body refresh 则尝试静默刷新。
 * 供路由守卫在 F5/深链时恢复内存令牌。
 */
export async function ensureAccessToken(): Promise<boolean> {
  const user = useUserStore()
  if (user.accessToken) return true
  if (!user.cookieSession && !user.refreshToken) return false
  return refreshOnce()
}

async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  // Cookie 会话:body 不带 refresh(服务端读 smart_rt);body 模式:必须有 refreshToken
  if (!user.cookieSession && !user.refreshToken) return false

  const headers: Record<string, string> = {}
  if (user.cookieSession || readCookie(CSRF_COOKIE)) {
    const csrf = readCookie(CSRF_COOKIE)
    if (csrf) headers[CSRF_HEADER] = csrf
  }

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    // Cookie 模式刷新令牌在 HttpOnly Cookie 里,body 的 refreshToken 留空:服务端 body 为空才读 Cookie
    body: user.cookieSession ? { refreshToken: '' } : { refreshToken: user.refreshToken },
    headers,
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  user.setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}

/** 401 → 刷新一次并重放原请求;刷新失败 → 清会话 + 跳登录。 */
const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // 带 body 的写请求发出前存一份可重放副本(GET/HEAD 无 body,不必克隆)。
    if (request.method !== 'GET' && request.method !== 'HEAD')
      replayable.set(request, request.clone())
    return request
  },
  async onResponse({ request, response }) {
    if (response.status !== 401) return response
    const url = request.url
    if (url.includes('/api/v1/auth/refresh') || url.includes('/api/v1/auth/login')) return response

    const ok = await refreshOnce()
    if (!ok) {
      useUserStore().clear()
      const { useAuthStore } = await import('#/stores/auth')
      useAuthStore().reset()
      const { router, resetRouter } = await import('#/router') // 惰性引入,避免与 router 静态循环依赖
      resetRouter()
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
      return response
    }
    // 重放:优先用发出前的克隆副本(body 未被消费),无副本的 GET 直接用原请求;裸 fetch 绕过中间件,手动补新令牌 + CSRF。
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: 'include' })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    if (isMutating(retry.method)) attachCsrf(retry.headers)
    return fetch(retry)
  },
}

/**
 * 403 + 40024(ReauthRequired) → 弹再认证 → 成功后重放一次。
 * 与 refresh 同形:依赖 onRequest 预克隆 body;重试头防死循环。
 */
const reauthMiddleware: Middleware = {
  async onResponse({ request, response }) {
    if (response.status !== 403) return response
    if (request.headers.get(REAUTH_RETRY_HEADER) === '1') return response
    if (request.url.includes('/api/v1/auth/reauth') || request.url.includes('/api/v1/auth/login')) {
      return response
    }

    let code: number | undefined
    try {
      const body = (await response.clone().json()) as { code?: number }
      code = body?.code
    } catch {
      return response
    }
    if (code !== REAUTH_REQUIRED_CODE) return response

    const granted = await requestReauth()
    if (!granted) return response

    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: 'include' })
    const token = useUserStore().accessToken
    if (token) retry.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(retry.method)) attachCsrf(retry.headers)
    retry.headers.set(REAUTH_RETRY_HEADER, '1')
    return fetch(retry)
  },
}

/** 内核同款中间件链:超时必须最先(后面两个要克隆最终 Request 做重放)→ Bearer + CSRF → 401 刷新重放 → 40024 再认证。 */
function attachMiddlewares<P extends {}>(c: Client<P>): Client<P> {
  c.use(timeoutMiddleware)
  c.use(authMiddleware)
  c.use(refreshMiddleware)
  c.use(reauthMiddleware)
  return c
}

/**
 * 按调用时的 runtime.apiBase 取客户端。客户端大多在模块顶层创建(`export const client = createApiClient()`),
 * 那时 createSmartAdmin 可能还没跑、apiBase 尚未落定;所以每次调用前核对一次,变了就按新地址重建,
 * 调用方后加的中间件(use)一并带过去。
 */
function followApiBase<P extends {}>(build: (baseUrl: string) => Client<P>): Client<P> {
  let base = runtime.apiBase
  let inner = build(base)
  const added: Middleware[] = []
  const current = () => {
    if (base !== runtime.apiBase) {
      base = runtime.apiBase
      inner = build(base)
      if (added.length) inner.use(...added)
    }
    return inner
  }
  return new Proxy({} as Client<P>, {
    get(_target, key) {
      if (key === 'use')
        return (...mw: Middleware[]) => {
          added.push(...mw)
          current().use(...mw)
        }
      if (key === 'eject')
        return (...mw: Middleware[]) => {
          for (const m of mw) {
            const i = added.indexOf(m)
            if (i >= 0) added.splice(i, 1)
          }
          current().eject(...mw)
        }
      const c = current() as unknown as Record<PropertyKey, unknown>
      const v = c[key]
      return typeof v === 'function' ? (v as (...args: unknown[]) => unknown).bind(c) : v
    },
  })
}

/**
 * 消费方自己端点的类型化客户端:与内核 client 同 apiBase、同一条中间件链,只是 paths 换成自己生成的 schema。
 * credentials:'include'——Cookie 会话(Session:CookieMode)静默刷新/登出依赖 HttpOnly refresh Cookie;
 * 同源默认也会带 Cookie,显式 include 覆盖 apiBase 跨源场景(需后端 CORS AllowCredentials)。
 */
export function createApiClient<P extends {}>(): Client<P> {
  return followApiBase(baseUrl =>
    attachMiddlewares(createClient<P>({ baseUrl, credentials: 'include' })),
  )
}

/** 内核客户端(内核端点的 paths)。 */
export const client = createApiClient<paths>()

// 刷新专用客户端:不挂刷新中间件,避免刷新自身 401 触发递归;
// 但也要有超时闸:它挂住时路由守卫会一起卡在 ensureAccessToken 上,整页停在白屏。
const bare = followApiBase(baseUrl => {
  const c = createClient<paths>({ baseUrl, credentials: 'include' })
  c.use(timeoutMiddleware)
  return c
})
