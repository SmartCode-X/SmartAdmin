import { useRouter } from 'vue-router'
import { useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useEventBus } from '@vueuse/core'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useUserStore } from '#/stores/user'
import { runtime } from '#/lib/runtime'

/**
 * 未读通知刷新总线:useRealtime 收到后端 `notice-changed` 推送后 emit,NoticeBell 订阅后立即重拉未读角标。
 * 解耦长连接与铃铛组件(二者互不 import 对方)。
 */
export const noticeBus = useEventBus<void>('notice-changed')

// 进程内单例连接:鉴权外壳(default.vue)挂载一次即建一条;重复 start 幂等(已连不再建)。
let connection: HubConnection | null = null
// 主动登出/改密重登:后端 Logout 也走 RevokeAsync → 推 force-logout;本页应静默,勿弹「您已被强制下线」。
// 其它同会话标签页仍会收到推送并提示(它们没调 beginVoluntaryLogout)。下次 start 清零。
let voluntaryLogout = false

/** 额外 hub 事件的处理器签名:后端 NotifyUserAsync / NotifyAllAsync 恒发单个 data 载荷。 */
export type RealtimeHandler = (data: unknown) => void

// 扩展模块登记的 hub 事件。必须能挂到内置这条连接上:另建一条连向同一个 /hub/realtime 的连接,
// 意味着一个在线用户两条 WebSocket,而后端是按 userId 群发到该用户的全部连接 ——
// notice-changed / force-logout 这类内置事件会被投递两遍。
const extraHandlers = new Map<string, Set<RealtimeHandler>>()
// 当前连接上已 conn.on 过的事件名:每建一条新连接清空,保证既不重复绑定也不漏绑。
let boundEvents = new Set<string>()

function bindExtra(conn: HubConnection, event: string): void {
  if (boundEvents.has(event)) return
  boundEvents.add(event)
  // 一个事件只挂一个派发器,退订只从 Set 里摘处理器,不调 conn.off(event) ——
  // 那会把同事件上其它模块的处理器一起摘掉。
  conn.on(event, (data: unknown) => {
    for (const handler of extraHandlers.get(event) ?? []) handler(data)
  })
}

/**
 * 在内置这条 SignalR 连接上登记自己的 hub 事件,返回退订函数。
 * <p>建连之前登记的于 start() 时统一绑定,之后登记的直接挂到现有连接;连接重建(登出后再登录)会自动重绑。
 * 组件里用记得在 onScopeDispose / onUnmounted 调返回值退订,否则热更新与路由切换会越挂越多。</p>
 */
export function onRealtime(event: string, handler: RealtimeHandler): () => void {
  const handlers = extraHandlers.get(event) ?? new Set<RealtimeHandler>()
  handlers.add(handler)
  extraHandlers.set(event, handlers)
  if (connection) bindExtra(connection, event)
  return () => {
    handlers.delete(handler)
    // 认一下手里这个 Set 还是不是表里那个:退订被调两次时,中间可能已有人重新登记同名事件,
    // 照删会把后来者的处理器一起摘掉,而且是静默的。
    if (handlers.size === 0 && extraHandlers.get(event) === handlers) extraHandlers.delete(event)
  }
}

async function stopConnection(): Promise<void> {
  const c = connection
  connection = null
  if (c) {
    try {
      await c.stop()
    } catch {
      /* 停止失败无害 */
    }
  }
}

/**
 * 主动登出前调用:标记自愿退出 + 先断 SignalR。
 * <p>后端会话吊销会推 `force-logout`(与管理员强退共用通道);本页断连后收不到,即便迟到推送也不弹强制下线文案。
 * 其它标签页仍可被踢并提示。</p>
 */
export async function beginVoluntaryLogout(): Promise<void> {
  voluntaryLogout = true
  await stopConnection()
}

/**
 * 实时通知客户端(SignalR)。仅在鉴权外壳挂载时 start、登出/卸载时 stop。
 * <p>后端 `SmartAdmin:Realtime:Enabled` 关闭时 Hub 不存在 → 初次连接失败,**静默退回** NoticeBell 的 30s 轮询兜底
 * 与「下次请求 401」惰性登出(纯增强,无回归)。令牌走 query `access_token`(浏览器 WebSocket 带不了 Authorization 头)。</p>
 * message/router/i18n 在 setup 期(default.vue)绑定,推送到达时复用,故务必在 setup 中调用本 composable。
 */
export function useRealtime() {
  const router = useRouter()
  const message = useMessage()
  const { t } = useI18n()

  function start() {
    if (connection || !useUserStore().accessToken) return
    voluntaryLogout = false
    const conn = new HubConnectionBuilder()
      .withUrl(`${runtime.apiBase}/hub/realtime`, {
        accessTokenFactory: () => useUserStore().accessToken,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    // 强制下线:清会话 + 授权态 + 提示 + 跳登录(与 api/client.ts 刷新失败路径同款收尾)
    conn.on('force-logout', () => {
      const silent = voluntaryLogout
      voluntaryLogout = false
      void stopConnection()
      useUserStore().clear()
      void import('#/stores/auth').then(({ useAuthStore }) => useAuthStore().reset())
      void import('#/router').then(({ resetRouter }) => resetRouter())
      // 主动登出也会触发后端 force-logout 推送:只静默清会话,不谎报「被强制下线」。
      if (!silent) message.warning(t('realtime.forcedLogout'))
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
    })
    // 公告变更:通知 NoticeBell 立即重拉未读(替代最长 30s 轮询延迟)
    conn.on('notice-changed', () => noticeBus.emit())
    // 扩展模块的事件:新连接从零绑一遍
    boundEvents = new Set()
    for (const event of extraHandlers.keys()) bindExtra(conn, event)

    connection = conn
    conn.start().catch(() => {
      // 初次连接失败(后端未开启实时 → Hub 404):静默退回轮询兜底,不重试刷屏。
      // withAutomaticReconnect 只在「连过又断」后重连,不重试初次 start,故这里不会造成对已关实时的后端反复叩门。
      if (connection === conn) connection = null
    })
  }

  return { start, stop: stopConnection }
}
