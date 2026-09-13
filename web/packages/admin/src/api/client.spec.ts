import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Middleware } from 'openapi-fetch'

// 客户端多在模块顶层创建,那时 createSmartAdmin 可能还没写入 apiBase;
// 钉住「调用时按当时的 apiBase 发请求」,以及地址变了重建后,调用方后加的中间件还在。
const fetchMock = vi.fn(async (_req: Request) => new Response('{}', { status: 200 }))

vi.mock('#/stores/user', () => ({ useUserStore: () => ({ accessToken: '' }) }))

beforeEach(() => {
  vi.resetModules()
  fetchMock.mockClear()
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

async function load() {
  const { runtime } = await import('#/lib/runtime')
  const { createApiClient } = await import('./client')
  return { runtime, createApiClient }
}

describe('createApiClient', () => {
  it('创建之后才设定的 apiBase,调用时生效', async () => {
    const { runtime, createApiClient } = await load()
    runtime.apiBase = ''
    const api = createApiClient<Record<string, never>>()

    runtime.apiBase = 'https://api.example.test'
    await (api as unknown as { GET: (p: string) => Promise<unknown> }).GET('/api/v1/ping')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0]![0].url).toBe('https://api.example.test/api/v1/ping')
  })

  it('apiBase 变了重建后,调用方加的中间件仍然生效', async () => {
    const { runtime, createApiClient } = await load()
    runtime.apiBase = ''
    const api = createApiClient<Record<string, never>>()
    const seen: string[] = []
    const mw: Middleware = {
      onRequest({ request }) {
        seen.push(request.url)
        return request
      },
    }
    api.use(mw)

    runtime.apiBase = 'https://api.example.test'
    await (api as unknown as { GET: (p: string) => Promise<unknown> }).GET('/api/v1/ping')

    expect(seen).toEqual(['https://api.example.test/api/v1/ping'])
  })
})
