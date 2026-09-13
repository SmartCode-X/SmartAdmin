# HTTP 请求层

链路只有这么长：一个 openapi-fetch 客户端加四个中间件，客户端的类型全由后端 OpenAPI 契约推出来。超时闸必须挂在最前面，往后依次是认证、401 刷新、403 短时再认证。最难写对的还是刷新那一段：令牌过期必须对业务代码完全隐形，401 之后自动刷新、重放，调用方连一次报错都看不到。

## 全景

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
api/schema.d.ts            生成的类型(paths,禁止手改)
  │
  ▼
api/client.ts              createApiClient<paths>():带类型的 openapi-fetch 客户端 + 四个中间件
  │
  ▼
api/<域>.ts                按领域分组的 API 函数,统一形态:client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError,经 translateError(err) 展示
```

这条链路内核和应用各走一遍。内核包里是 `api/schema.d.ts`（对外导出为 `KernelPaths`）、`api/client.ts` 的 `client` 和 `api/index.ts` 的内置端点。应用里是自己生成的 `src/api/schema.d.ts`、`src/api/client.ts` 里的 `createApiClient<paths>()` 和 `src/api/<域>.ts`，`unwrap` 这些原语从 `smart-admin-web` 导入。两边的客户端挂的是同一条中间件链。

图里下面两格，是响应回来之后的事。`unwrap` 把后端两种响应形状收拢成一个结果。视图层的 `translateError` 把错误码变成展示文案。要是你找的是这两段，去[对接后端响应](/zh/frontend/api-contract)。这里只讲请求怎么带着类型和令牌发出去，到 `client.ts` 为止。

## 重新生成契约：`gen:api`

```bash
npm run gen:api                                          # 默认 http://localhost:5100
SMART_API_TARGET=http://localhost:5200 npm run gen:api   # 后端在别处
npm run gen:api -- --target http://127.0.0.1:5200        # 只这一次，不改环境变量
```

- 后端必须先跑起来。脚本要向一个真实运行中的服务器拉 `/openapi/v1.json`，默认地址是 `http://localhost:5100`。
- 后端跑在别处时，用 `SMART_API_TARGET` 指过去——**和 dev 代理是同一个变量**（`vite.config.ts`、`dev-start.sh` 都读它），所以代理和契约生成不会各指一处。只想临时换一次就用 `--target`，它的优先级最高。
- 生成的 `schema.d.ts` 是**生成产物**，禁止手改。改了后端的接口或 DTO，重新生成一遍就行。手改的东西，下次生成会被无声覆盖。在内核仓的 `web/` 下跑，写的是包里的 `src/api/schema.d.ts`；在应用里跑，写的是应用自己的 `src/api/schema.d.ts`，内核端点和你的端点都在里面。
- `createApiClient<paths>()` 拿这份文件的 `paths` 当类型源。所以每一次 `client.GET/POST/PUT/DELETE` 调用，从路径参数、查询参数、请求体到响应形状，全链路的类型都是后端真实契约推出来的。

## 类型化客户端与四个中间件

```ts
function attachMiddlewares<P extends {}>(c: Client<P>): Client<P> {
  c.use(timeoutMiddleware)
  c.use(authMiddleware)
  c.use(refreshMiddleware)
  c.use(reauthMiddleware)
  return c
}

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
```

`baseUrl` 取自 `runtime.apiBase`，默认为空。schema 的 path 键本身已经带了 `/api/v1`，`/api` 走同源。开发时 Vite 把它代理到后端，生产时靠反代或后端自托管。只有前端和 API 真的跨域，才需要给 `createSmartAdmin({ apiBase })` 传地址，模板的 `main.ts` 从构建期的 `VITE_API_BASE` 取这个值。客户端大多在模块顶层创建，那时 `createSmartAdmin()` 可能还没跑。所以 `followApiBase` 每次调用前核对一次 `apiBase`，变了就按新地址重建，调用方后加的中间件一并带过去。`credentials: 'include'` 是给 Cookie 会话用的，它的静默刷新和登出靠 HttpOnly 的 refresh Cookie。

四个中间件由 `attachMiddlewares` 按顺序挂上，`createApiClient()` 建出来的客户端都有；`bare` 只挂超时闸，理由见下文：

### 超时闸，必须最先挂

```ts
const timeoutMiddleware: Middleware = {
  onRequest({ request }) {
    const ms = (request as Request & { timeout?: unknown }).timeout
    const limit = typeof ms === 'number' && ms > 0 ? ms : DEFAULT_TIMEOUT_MS
    return new Request(request, {
      signal: AbortSignal.any([request.signal, AbortSignal.timeout(limit)]),
    })
  },
}
```

默认 30 秒；上传、分片、导出、导入提交这类天然慢的调用，在 api 层显式传一个 `timeout`（毫秒），openapi-fetch 会把这类自定义键原样挂到 `Request` 上。这道闸必须**最先**注册：后面的重放中间件要克隆的是最终发出的那个 `Request`，顺序反了就克隆到一份还没挂超时信号的旧请求。调用方自己传的 `signal` 依然生效，`AbortSignal.any` 把它和超时信号并起来，谁先触发算谁，不是拿超时顶掉调用方的取消能力。`bare` 也单独挂了这一道：刷新请求自己卡死，路由守卫的 `ensureAccessToken` 会跟着一起卡在原地，整页停在白屏。

### 认证中间件，外加写请求的 CSRF

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(request.method)) attachCsrf(request.headers)
    return request
  },
}
```

令牌是请求发出时才去 store 读的，不是模块加载时读一次就存住。所以每次都能拿到最新的令牌，包括刚刚才刷新出来的那个。`isMutating` 排除 `GET`/`HEAD`/`OPTIONS`，其余写请求都会尝试附上 CSRF 头：`attachCsrf` 读 `document.cookie` 里可读的 `smart_csrf`（双提交 CSRF，与后端 `AuthCookieNames` 对齐），有就塞进 `X-Smart-CSRF` 头，没有就不附。同源默认模式没有这块 Cookie，这一步天然是空操作。

### 401 刷新中间件，以及为什么重放需要一份克隆

这是 `client.ts` 里最不直观的一段。问题出在 `Request` 的 body 上，它是个流，只能读一次。一个 POST/PUT 请求被判 401 之后，流程要先刷新令牌，再拿同一个请求重放。可等响应回来的时候，原始请求的 body 早就被 `fetch` 读掉了，原样重放会把 body 发丢。

解法是在请求真正发出**之前**、body 流还没被碰过的时候，先克隆一份：

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // 只有带 body 的写请求才需要留一份可重放副本 —— GET/HEAD 没有 body 可丢。
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
```

`Request.clone()` 把底层的 body 流一分为二，两份各自独立可读。原始请求照常发出去，没被动过的那份克隆存进一个 `WeakMap`，键就是原始 `Request` 实例。openapi-fetch 会把这同一个实例一路带到 `onResponse`。请求结束后，这个 `WeakMap` 条目自动回收，不用手动清理。

遇到 401，依次发生：

1. **跳过刷新和登录接口自身**：`/auth/refresh` 或 `/auth/login` 返回的 401 是真实的凭证失败，不是令牌过期。把它也塞进刷新流程，会死循环。
2. **`refreshOnce()`**：并发合流。同一时刻要是有好几个请求一起 401，也只发一次 `/auth/refresh`，让大家都等同一个 promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **刷新失败**（没有 refreshToken、网络错误、`code` 非零，或没有 `data`）：清空 `user` 的令牌，`auth.reset()` 清掉授权态，`resetRouter()` 拆掉动态路由，再跳转 `/login`。这里 router 用的是惰性 import，免得和 `client.ts` 形成静态循环依赖。
4. **刷新成功**：用发出前存的克隆副本重建请求，补上刚刷新出来的新令牌，写请求再重新附一次 CSRF 头，再用**裸 `fetch()`** 重放。GET/HEAD 本来就没克隆，直接用原始请求。这里特意不再走一次 `client.GET/POST(...)`。再走 `client`，后面几个中间件会在这次重放上又跑一遍。万一新令牌也被拒，又是一次 401，就会递归进下一轮刷新。

### 为什么 `doRefresh` 用 `bare` 而不是 `client`

```ts
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
```

`bare` 是用同一份 schema 建的第二个 `openapi-fetch` 客户端，**不挂认证 / 刷新 / 再认证这三个中间件**（超时闸例外，两个客户端都挂）。刷新请求走 `bare`，好处是它不会递归。就算刷新本身失败，比如 refreshToken 也过期了、接口照样答 401，这个 401 也进不了 `refreshMiddleware.onResponse`，因为 `bare` 上根本没有认证/刷新中间件链可以递归。`onResponse` 里那道跳过 `/auth/refresh`/`/auth/login` 的 URL 判断只是第二道保险，顺带覆盖经 `client` 调用登录失败的情况。刷新请求自身能防住递归，根子上靠的是它压根不在 `client` 的中间件链上。

正因为 `bare` 没有 `authMiddleware`，`doRefresh` 才要自己手动附 CSRF 头。Cookie 会话下服务端从 `smart_rt` 这个 HttpOnly Cookie 读刷新令牌，body 留空即可；默认模式下服务端只认请求体、不读 Cookie，body 必须带 `refreshToken`。

### 403 短时再认证（reauth）

刷新解决的是令牌过期，`reauthMiddleware` 解决的是另一件事：某些操作即使令牌还有效，也要求用户当场再证明一次身份（TOTP 或密码）。后端遇到这类操作不是简单拒绝，而是回 403 + 业务码 `40024`（`ReauthRequired`）：

```ts
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
```

形状和刷新中间件很像：一样看 `replayable` 里有没有留一份克隆，重放时补令牌、补 CSRF。区别在命中之后怎么办。它不去刷新令牌，而是调 `reauthGate.ts` 的 `requestReauth()`，把决定权交给 UI：应用挂载时，`ReauthModal` 之类的宿主组件用 `registerReauthHandler` 注册一个弹窗处理函数，弹出来收一次 TOTP 或密码，成功就 resolve `true`。并发的多个 403 只弹一次，共享同一个 `inflight` Promise。拿到 `granted` 才重放原请求，重放请求上带一个 `X-Smart-Reauth-Retried` 头，`reauthMiddleware` 一看到这个头就直接放行响应，不会对同一个请求二次弹窗。

## 开发代理与 CORS

类型化客户端默认浏览器是同源访问 `/api` 的：`apiBase` 默认为空，请求走的是一个看起来相对的 URL，没做任何跨域处理。`gen:api` 不一样，它压根不经过浏览器：Node 直接去拉 `SMART_API_TARGET`（默认 `http://localhost:5100`）的 `/openapi/v1.json`，不走 dev proxy，自然也就没有 CORS 这回事。本地开发时，后端跑在 `:5100`，dev server 跑在 `:5173`，端口不一样。总得有个东西把这道缝补上，两边才能对得上。

补这道缝的就是应用 `vite.config.ts` 里的 dev 代理：

```ts
const apiTarget = process.env.SMART_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
  },
},
```

它把 `:5173` 上的 `/api/*`、`/openapi/*`、`/hub/*` 请求转发给后端。浏览器自始至终只看到一个源，也就是 `:5173`，自然不存在跨域问题。目标地址默认是 `http://localhost:5100`。后端跑在别处时，启动 Vite 前设一下 `SMART_API_TARGET` 就行。

没有这层代理会怎样？类型化客户端的请求会直接打到后端的源上。后端 CORS 默认 deny-all，响应还没传到 `unwrap`，就被浏览器拒了。是这层代理，让请求层「同源」这个前提在本地成立。

::: tip 生产环境没有这层代理
`npm run dev` 的代理只在开发期存在。应用构建出的 `dist` 是纯静态文件，请求怎么到后端，要在部署时自己解决。后端顺带托管前端产物，或者 nginx/Caddy 反代，都是同源，不用配 CORS。只有前端和后端真跨源，比如前端上 CDN、后端独立域名，才需要动 `SmartAdmin:Api:Cors:AllowedOrigins`，方案见[部署路线 C：真跨源](/zh/guide/deployment/route-c)。
:::

完整代理配置、内核仓里的源码别名，见[项目结构与启动](/zh/frontend/structure)。
