import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, defineComponent, h, nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createI18n } from 'vue-i18n'

const { conn, handlers } = vi.hoisted(() => {
  const map: Record<string, (data?: unknown) => void> = {}
  const connection = {
    on: (event: string, cb: (data?: unknown) => void) => {
      map[event] = cb
    },
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
  }
  return { conn: connection, handlers: map }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return conn
    }
  },
  LogLevel: { Warning: 3 },
}))

// 强退分支里那句 `void import('#/router')` 是刻意不 await 的浮动导入。真实运行没问题,
// 但在用例里它可能在测试环境拆掉之后才落地,于是整个 spec 挂在
// EnvironmentTeardownError(「环境已拆除,还在加载 /src/router/routes.ts」)上。
// 本机快、CI 慢,所以只在 CI 上炸。把它 mock 成即刻可解析的桩,顺带也不必真去加载整棵路由表。
const { resetRouter } = vi.hoisted(() => ({ resetRouter: vi.fn() }))
vi.mock('#/router', () => ({ resetRouter }))
const warning = vi.fn()
vi.mock('naive-ui', async orig => {
  const actual = await orig<typeof import('naive-ui')>()
  return {
    ...actual,
    useMessage: () => ({
      warning,
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      loading: vi.fn(),
    }),
  }
})

import { beginVoluntaryLogout, onRealtime, useRealtime } from './useRealtime'
import { useUserStore } from '#/stores/user'

function mountRealtime() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  })
  const i18n = createI18n({
    legacy: false,
    locale: 'zh-CN',
    messages: { 'zh-CN': { realtime: { forcedLogout: '您已被强制下线' } } },
  })
  const pinia = createPinia()
  setActivePinia(pinia)
  useUserStore().$patch({
    accessToken: 't',
    refreshToken: 'r',
    userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false },
  } as never)

  const Host = defineComponent({
    setup() {
      const { start } = useRealtime()
      start()
      return () => h('div')
    },
  })
  const app = createApp(Host)
  app.use(pinia)
  app.use(router)
  app.use(i18n)
  const el = document.createElement('div')
  app.mount(el)
  return { app, router }
}

beforeEach(() => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  Object.keys(handlers).forEach(k => delete handlers[k])
})

afterEach(() => {
  document.body.innerHTML = ''
})

describe('useRealtime force-logout', () => {
  it('非自愿 force-logout:清会话 + 弹强制下线', async () => {
    const { app } = mountRealtime()
    await nextTick()
    expect(handlers['force-logout']).toBeTruthy()

    handlers['force-logout']()
    await nextTick()
    // force-logout 里动态 import auth/router 是微任务,再等一拍
    await Promise.resolve()
    await nextTick()

    expect(useUserStore().accessToken).toBe('')
    expect(warning).toHaveBeenCalled()
    // 动态导入被 mock 掉了,顺手验一下「强退确实重置了路由」。
    // 用 waitFor 而不是再数一拍微任务:两个动态 import 串在一起,拍数是实现细节。
    await vi.waitFor(() => expect(resetRouter).toHaveBeenCalled())
    app.unmount()
  })

  it('beginVoluntaryLogout 后 force-logout 静默:清会话但不弹强制下线', async () => {
    const { app } = mountRealtime()
    await nextTick()

    await beginVoluntaryLogout()
    expect(conn.stop).toHaveBeenCalled()

    handlers['force-logout']()
    await nextTick()
    await Promise.resolve()
    await nextTick()

    expect(useUserStore().accessToken).toBe('')
    expect(warning).not.toHaveBeenCalled()
    app.unmount()
  })
})

// 扩展模块的事件必须能挂到内置这条连接上:另建一条连向同一 hub 的连接会让内置事件被投递两遍
// (后端按 userId 群发到该用户的全部连接)。
describe('onRealtime 注册接缝', () => {
  it('建连前登记 → start 时统一绑定,推送带 data 送达', async () => {
    const seen: unknown[] = []
    const off = onRealtime('job-progress', d => seen.push(d))

    const { app } = mountRealtime()
    await nextTick()

    handlers['job-progress']({ percent: 42 })
    expect(seen).toEqual([{ percent: 42 }])

    off()
    app.unmount()
  })

  it('建连后登记 → 直接挂到现有连接', async () => {
    const { app } = mountRealtime()
    await nextTick()
    expect(handlers['late-event']).toBeUndefined()

    const seen: unknown[] = []
    const off = onRealtime('late-event', d => seen.push(d))
    handlers['late-event']('x')

    expect(seen).toEqual(['x'])
    off()
    app.unmount()
  })

  it('退订只摘自己:同事件的其它处理器照旧收到', async () => {
    const a: unknown[] = []
    const b: unknown[] = []
    const offA = onRealtime('shared', d => a.push(d))
    const offB = onRealtime('shared', d => b.push(d))

    const { app } = mountRealtime()
    await nextTick()

    offA()
    handlers['shared'](1)

    expect(a).toEqual([])
    expect(b).toEqual([1])
    offB()
    app.unmount()
  })

  it('退订调两次不误伤后来者:期间重新登记的同名事件照收', async () => {
    const first: unknown[] = []
    const offFirst = onRealtime('respawn', d => first.push(d))
    offFirst()

    const later: unknown[] = []
    const offLater = onRealtime('respawn', d => later.push(d))
    offFirst() // 组件里 onUnmounted 与手动退订各调一次,现实中会发生

    const { app } = mountRealtime()
    await nextTick()
    handlers['respawn']('still here')

    expect(later).toEqual(['still here'])
    offLater()
    app.unmount()
  })

  it('连接重建(登出后再登录)自动重绑,不需要重新登记', async () => {
    const seen: unknown[] = []
    const off = onRealtime('job-progress', d => seen.push(d))

    const first = mountRealtime()
    await nextTick()
    await beginVoluntaryLogout()
    first.app.unmount()

    const second = mountRealtime()
    await nextTick()
    handlers['job-progress']('after-reconnect')

    expect(seen).toEqual(['after-reconnect'])
    off()
    second.app.unmount()
  })
})
