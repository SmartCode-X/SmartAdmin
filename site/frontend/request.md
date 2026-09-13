# HTTP Request Layer

Every backend call in the frontend flows through the same pipeline: an openapi-fetch client typed from the backend's OpenAPI contract, plus four middlewares, in this order — a timeout guard (must be registered first), auth, 401 refresh, and 403 short-lived re-authentication. This page covers how that pipeline is assembled, why the middlewares are written the way they are, and why local-dev requests reach the backend without any CORS setup.

## The big picture

```text
backend OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
api/schema.d.ts            generated types (paths, do not hand-edit)
  │
  ▼
api/client.ts              createApiClient<paths>(): typed openapi-fetch client + four middlewares
  │
  ▼
api/<domain>.ts            domain-grouped API functions, all shaped: client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError, display via translateError(err)
```

The kernel and the app each run this chain once. Inside the kernel package it's `api/schema.d.ts` (exported as `KernelPaths`), the `client` in `api/client.ts`, and the built-in endpoints in `api/index.ts`. In the app it's the app's own generated `src/api/schema.d.ts`, `createApiClient<paths>()` in `src/api/client.ts`, and `src/api/<domain>.ts`, with primitives like `unwrap` imported from `smart-admin-web`. Both clients carry the same middleware chain.

The bottom two rows of that diagram — `unwrap` and the view layer's `translateError` — are what happens *after* the response comes back: how the backend's two response shapes are collapsed into one result, and how an error code becomes display text. Those are split off into [Backend Contract & Error Codes](/frontend/api-contract). This page stops at `client.ts` — i.e. how a request goes out, typed and carrying its token.

## Regenerating the contract: `gen:api`

```bash
npm run gen:api                                          # defaults to http://localhost:5100
SMART_API_TARGET=http://localhost:5200 npm run gen:api   # backend elsewhere
npm run gen:api -- --target http://127.0.0.1:5200        # just this once, no env var
```

- The backend must be running first — the script fetches `/openapi/v1.json` from a live server, `http://localhost:5100` by default.
- When the backend runs elsewhere, point `SMART_API_TARGET` at it — **the same variable the dev proxy reads** (`vite.config.ts` and `dev-start.sh` both use it), so the proxy and the contract generator can't drift apart. For a one-off, `--target` takes precedence.
- The generated `schema.d.ts` is a **generated artifact** — never hand-edit it — change the backend endpoint/DTO and regenerate; hand edits are silently overwritten on the next run. Run from the kernel repo's `web/`, it writes the package's `src/api/schema.d.ts`; run in an app, it writes the app's own `src/api/schema.d.ts`, which holds the kernel endpoints and yours alike.
- `createApiClient<paths>()` takes that file's `paths` as its type source, so every `client.GET/POST/PUT/DELETE` call is typed end to end — path params, query params, request body, and response shape all derive from the backend's actual contract.

## The typed client and its four middlewares

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

/** The kernel client (typed against the kernel endpoints' paths). */
export const client = createApiClient<paths>()

// Refresh-only client: no refresh middleware, so the refresh call's own 401 can't recurse;
// it still needs the timeout guard — if it hangs, the router guard hangs on ensureAccessToken and the page stays blank.
const bare = followApiBase(baseUrl => {
  const c = createClient<paths>({ baseUrl, credentials: 'include' })
  c.use(timeoutMiddleware)
  return c
})
```

`baseUrl` comes from `runtime.apiBase` and defaults to empty — the schema's path keys already include `/api/v1`, and `/api` is same-origin (proxied to the backend in dev, reverse-proxied or self-hosted by the backend in production). Only when the frontend and the API are genuinely cross-origin do you pass an address to `createSmartAdmin({ apiBase })`; the template's `main.ts` takes it from the build-time `VITE_API_BASE`. Clients are mostly created at module top level, possibly before `createSmartAdmin()` has run, so `followApiBase` checks `apiBase` before every call and rebuilds the client when it has changed, carrying over any middleware the caller added later. `credentials: 'include'` is there for Cookie sessions, whose silent refresh and logout depend on the HttpOnly refresh cookie.

`attachMiddlewares` registers all four middlewares in order, so every client built by `createApiClient()` has them; `bare` only gets the timeout guard — see below for why:

### The timeout guard, which must go first

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

The default is 30 seconds; a naturally slow call — an upload, a chunk, an export, an import submission — passes an explicit `timeout` (in milliseconds) at the API layer, and openapi-fetch carries that custom key straight through onto the `Request`. This guard has to be registered **first**: the replay middlewares further down clone the final `Request` that's actually sent, and get the order wrong and they'd clone a stale request that never got the timeout signal attached. A caller's own `signal` still works — `AbortSignal.any` merges it with the timeout signal, whichever fires first wins, rather than the timeout overriding the caller's own cancellation. `bare` gets this guard too, on its own: if the refresh call itself hangs, the router guard's `ensureAccessToken` hangs right along with it, and the whole page sits on a blank screen.

### Auth middleware, plus CSRF on mutating requests

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

The token is read from the store at request time (not read once and cached at module load), so every request gets the current token — including one that was just refreshed. `isMutating` excludes `GET`/`HEAD`/`OPTIONS`; every other write attempts to attach a CSRF header — `attachCsrf` reads the readable `smart_csrf` cookie (double-submit CSRF, named to match the backend's `AuthCookieNames`) from `document.cookie` and sets it on `X-Smart-CSRF` if present, doing nothing otherwise — the default same-origin mode never has that cookie, so this step is naturally a no-op there.

### The 401 refresh middleware, and why replay needs a clone

This is the least obvious part of `client.ts`. The problem: a `Request`'s body is a stream that can only be read once. After a POST/PUT is hit with a 401, the flow needs to refresh the token and replay the *same request* — but by the time the response comes back, `fetch` has long since consumed the original request's body, and replaying it as-is would send an empty body.

The fix is to clone the request **before** it actually goes out, while the body stream is still untouched:

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // Only write requests with a body need a replay copy — GET/HEAD have no body to lose.
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
      const { router, resetRouter } = await import('#/router') // lazy import, avoids a static cycle with router
      resetRouter()
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
      return response
    }
    // Replay: prefer the pre-send clone (body untouched); a GET with no clone uses the original request;
    // a raw fetch bypasses the middlewares, so the new token + CSRF are attached by hand.
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: 'include' })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    if (isMutating(retry.method)) attachCsrf(retry.headers)
    return fetch(retry)
  },
}
```

`Request.clone()` tees the underlying body stream into two independently readable copies — the original goes out over the wire as usual, and the untouched clone is stashed in a `WeakMap` keyed by the original `Request` instance (openapi-fetch carries that same instance all the way through to `onResponse`; once the request finishes, the `WeakMap` entry is garbage-collected automatically, no manual cleanup).

On a 401, in order:

1. **Skip the refresh/login endpoints themselves** — a 401 from `/auth/refresh` or `/auth/login` is a genuine credential failure, not an expired token; feeding it into the refresh flow too would loop.
2. **`refreshOnce()`** — single-flight coalescing: if several requests 401 at the same moment, only one `/auth/refresh` call goes out, and they all await the same promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **Refresh fails** (no refresh token, network error, non-zero `code`, or no `data`) — clear the `user` store's tokens, `auth.reset()` the authorization state, `resetRouter()` to tear down the dynamic routes, then redirect to `/login` (the router is lazily imported to avoid a static circular dependency with `client.ts`).
4. **Refresh succeeds** — rebuild the request from the clone stashed before it was sent (GET/HEAD never cloned, so the original request is used directly), stamp on the freshly-refreshed token, reattach CSRF for a write request, and replay it with a **raw `fetch()`** — not another `client.GET/POST(...)`. Going back through `client` would re-run the later middlewares on this replay, and if the new token were also rejected (another 401), it would recurse into the next round of refresh.

### Why `doRefresh` uses `bare`, not `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  // Cookie session: no refresh in the body (the backend reads smart_rt instead); body mode needs one.
  if (!user.cookieSession && !user.refreshToken) return false

  const headers: Record<string, string> = {}
  if (user.cookieSession || readCookie(CSRF_COOKIE)) {
    const csrf = readCookie(CSRF_COOKIE)
    if (csrf) headers[CSRF_HEADER] = csrf
  }

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    // In Cookie mode the refresh token lives in the HttpOnly cookie, so refreshToken stays empty:
    // the backend reads the cookie only when the body is empty.
    body: user.cookieSession ? { refreshToken: '' } : { refreshToken: user.refreshToken },
    headers,
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  user.setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` is a second `openapi-fetch` client built from the same schema, with **the auth, refresh, and reauth middlewares left off** (the timeout guard is the exception — both clients get it). Routing the refresh request through `bare` means a failed refresh (say, the refresh token itself has expired and the endpoint answers 401 too) never re-enters `refreshMiddleware.onResponse` at all — there's no auth/refresh middleware chain on `bare` to recurse into. The URL check in `onResponse` that skips `/auth/refresh`/`/auth/login` is a second line of defense, one that incidentally also covers login failures called through `client`; the refresh request's own recursion-safety comes, fundamentally, from it not being on `client`'s middleware chain in the first place.

Because `bare` has no `authMiddleware`, `doRefresh` attaches the CSRF header itself: in Cookie-session mode the backend reads the refresh token off the HttpOnly `smart_rt` cookie, so the body can stay empty; in the default mode the backend reads only the request body and never the cookie, so the body must carry `refreshToken`.

### 403 short-lived re-authentication (reauth)

Refresh handles an expired token; `reauthMiddleware` handles something else — some actions demand the user prove their identity again, on the spot (TOTP or password), even with a perfectly valid token. Instead of a flat rejection, the backend answers those with a 403 plus business code `40024` (`ReauthRequired`):

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

It's shaped much like the refresh middleware — the same look-up in `replayable` for a stashed clone, the same token/CSRF reattachment on replay. What differs is what happens on a hit: instead of refreshing a token, it calls `requestReauth()` from `reauthGate.ts` and hands the decision to the UI. A host component such as `ReauthModal`, mounted somewhere in the app, registers a handler with `registerReauthHandler` that pops a modal, collects a TOTP code or password, and resolves `true` on success. Concurrent 403s only pop one modal, sharing the same `inflight` promise. Only once `granted` comes back does it replay the original request, this time carrying an `X-Smart-Reauth-Retried` header — `reauthMiddleware` sees that header and lets the response through unconditionally, so the same request never triggers a second modal.

## Dev proxy & CORS

The typed client assumes the browser is talking same-origin to `/api` — `apiBase` defaults to empty, and the client does no cross-origin handling of its own. `gen:api` is a different story: it's a Node script that fetches `/openapi/v1.json` straight from `SMART_API_TARGET` (default `http://localhost:5100`), doesn't go through the dev proxy, and isn't subject to CORS at all. In local dev the backend runs on `:5100` and the dev server on `:5173`, different ports, so something has to bridge that gap before the typed client can work.

That something is the dev proxy in the app's `vite.config.ts`:

```ts
const apiTarget = process.env.SMART_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR notification hub; ws:true proxies the WebSocket upgrade
  },
},
```

It forwards `/api/*`, `/openapi/*`, and `/hub/*` requests on `:5173` to the backend, so the browser only ever sees one origin (`:5173`) — no cross-origin problem to speak of. The target defaults to `http://localhost:5100`; if the backend runs elsewhere, set `SMART_API_TARGET` before starting Vite.

Without this proxy, the typed client's requests would hit the backend's origin directly — and the backend's CORS defaults to deny-all, so the browser would reject the response before it ever reached `unwrap`. It's this proxy that makes the request layer's "same-origin" assumption hold in local dev.

::: tip There's no proxy in production
The `npm run dev` proxy exists only during development. The app's production build in `dist` is plain static files, and how requests reach the backend is something you solve at deploy time: the backend serving the frontend build alongside it, or an nginx/Caddy reverse proxy — both same-origin, no CORS needed. Only when the frontend and backend are genuinely cross-origin (frontend on a CDN, backend on its own domain) do you touch `SmartAdmin:Api:Cors:AllowedOrigins`; for that setup, see [Deployment Route C: Genuinely Cross-Origin](/guide/deployment/route-c).
:::

For the full proxy config and the kernel repo's source alias, see [Project Structure & Startup](/frontend/structure).
