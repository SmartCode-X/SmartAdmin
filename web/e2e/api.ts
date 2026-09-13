import type { APIRequestContext } from '@playwright/test'
import { ADMIN_ACCOUNT, ADMIN_PASSWORD } from './helpers'

/** Set by playwright.config.ts to the unique host URL for this run. */
const apiBase = () => process.env.SMART_E2E_API_BASE ?? 'http://127.0.0.1:5101'

type Envelope<T> = { code: number; msg?: string; data?: T; args?: Record<string, unknown> }

async function readEnvelope<T>(res: {
  ok: () => boolean
  status: () => number
  json: () => Promise<unknown>
}): Promise<Envelope<T>> {
  const body = (await res.json()) as Envelope<T>
  if (!res.ok() && body?.code === undefined) {
    throw new Error(`HTTP ${res.status()} without envelope`)
  }
  return body
}

/**
 * 超管令牌,整个 worker 只登录一次。
 *
 * 令牌与 request 上下文无关(就是个 JWT 串),跨用例、跨 spec 复用没有副作用;`workers=1` 时
 * 这里就是全套跑一次。之所以要缓存而不是每个用例登一次:认证端点按客户端 IP 限流
 * (`Security:RateLimit.AuthPermitPerWindow`,默认 60 秒 20 次),整套逐个登录会在第二十来次
 * 开始成片拿到 code=40008 —— 那个信封里连 msg/msgKey 都没有,报出来是一句
 * `admin login failed: code=40008 msg=undefined`,活像密码错了,排查方向直接被带偏。
 * `playwright.config.ts` 已把 e2e 宿主的这道限流关掉,本缓存是第二道保险,顺带省掉十几次登录。
 */
let cachedAdminToken: Promise<string> | null = null

export function apiAdminToken(request: APIRequestContext): Promise<string> {
  cachedAdminToken ??= requestAdminToken(request)
  return cachedAdminToken
}

async function requestAdminToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${apiBase()}/api/v1/auth/login`, {
    data: { account: ADMIN_ACCOUNT, password: ADMIN_PASSWORD },
  })
  const env = await readEnvelope<{ accessToken: string }>(res)
  if (env.code !== 0 || !env.data?.accessToken) {
    throw new Error(`admin login failed: code=${env.code} msg=${env.msg}`)
  }
  return env.data.accessToken
}

export async function apiCreateUser(
  request: APIRequestContext,
  token: string,
  input: { account: string; name: string; password: string; forceTotp?: boolean },
): Promise<number> {
  const res = await request.post(`${apiBase()}/api/v1/sys/user`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      account: input.account,
      name: input.name,
      password: input.password,
      enabled: true,
      forceTotp: input.forceTotp ?? true,
      roleIds: [],
    },
  })
  const env = await readEnvelope<{ id: number }>(res)
  if (env.code !== 0 || env.data?.id == null) {
    throw new Error(`create user failed: code=${env.code} msg=${env.msg}`)
  }
  return env.data.id
}

/** 建 ForceTotp 用户供自助绑定 e2e(ADR 0006:无邀请)。宿主须启用 Totp:Enabled。 */
export async function seedForceTotpUser(request: APIRequestContext): Promise<{
  account: string
  password: string
  userId: number
}> {
  const account = `e2e_mfa_${Date.now().toString(36)}`
  const password = 'TestPass123!'
  const admin = await apiAdminToken(request)
  const userId = await apiCreateUser(request, admin, {
    account,
    name: 'E2E MFA User',
    password,
    forceTotp: true,
  })
  return { account, password, userId }
}

/**
 * 运行时开合 TOTP 能力(配置中心的 `sys.security.totp.enabled`)。
 *
 * 刻意不走部署级的 `Security:Totp:Enabled`:那一把是「硬开地板」,一旦为真运行时就关不掉,
 * 而 TOTP 开着的时候 `[RequireReauth]` 会在建用户、角色授权、配置写、菜单写、强退这些端点上
 * 全部生效——整套管理端用例会成片拿到 40024。
 */
export async function apiSetTotpEnabled(
  request: APIRequestContext,
  token: string,
  enabled: boolean,
): Promise<void> {
  const res = await request.put(`${apiBase()}/api/v1/sys/config/batch`, {
    headers: { Authorization: `Bearer ${token}` },
    data: [{ configKey: 'sys.security.totp.enabled', configValue: enabled ? 'true' : 'false' }],
  })
  const env = await readEnvelope(res)
  if (env.code !== 0) {
    throw new Error(`toggle totp(${enabled}) failed: code=${env.code} msg=${env.msg}`)
  }
}

/**
 * 拿一份短时再认证授予。TOTP 开着的时候,配置写这类端点挂着 `[RequireReauth]`,
 * 没有授予就是 40024。超管没绑 TOTP,所以走密码这一路。
 */
export async function apiReauthWithPassword(
  request: APIRequestContext,
  token: string,
  password: string = ADMIN_PASSWORD,
): Promise<void> {
  const res = await request.post(`${apiBase()}/api/v1/auth/reauth`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { method: 'password', password },
  })
  const env = await readEnvelope(res)
  if (env.code !== 0) {
    throw new Error(`reauth failed: code=${env.code} msg=${env.msg}`)
  }
}
