// Markdown 正文里链接的点击分流。通知正文写 `[查看详情](/system/notice)` 这种站内相对链接时,
// 浏览器默认整页跳转:动态路由丢失 → 守卫重建 → 页面闪一下,多标签状态全部重置。
// 这里按 href 形状分三类,由 MarkdownView 在容器上做一次事件委托,正文里的 <a> 不必逐个改。
import { isHttpUrl } from '#/utils/url'

export type MarkdownLinkKind =
  /** 以单个 `/` 开头的站内相对路径:走 vue-router */
  | { kind: 'route'; to: string }
  /** http(s) 绝对地址:新标签打开(noopener) */
  | { kind: 'external'; href: string }
  /** 其余(锚点、mailto:、tel:、协议相对 //…):交给浏览器,不干预 */
  | { kind: 'native' }

/**
 * 只认"单个 `/` 开头"为站内路径:`//evil.com` 与 `/\evil.com` 都是协议相对 URL,
 * 交给 router.push 会被当路径,交给浏览器会跳出站,两边都不该碰。
 */
export function classifyMarkdownLink(href: string | null | undefined): MarkdownLinkKind {
  const h = (href ?? '').trim()
  if (/^\/(?![/\\])/.test(h)) return { kind: 'route', to: h }
  if (isHttpUrl(h)) return { kind: 'external', href: h }
  return { kind: 'native' }
}

/** 修饰键或非左键:用户明确要"新开标签 / 另存",按浏览器原生语义办。 */
function wantsNative(ev: MouseEvent): boolean {
  return ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey
}

/**
 * 容器级 click 委托。命中 `<a href>` 且是站内路径时拦截默认行为改走 `push`;
 * http(s) 外链改为新标签打开;其它情况(含 Ctrl/Cmd/Shift + 点击)原样放行。
 * 返回值是"是否接管了这次点击",便于单测钉住分流结果。
 */
export function handleMarkdownLinkClick(ev: MouseEvent, push: (to: string) => void): boolean {
  if (ev.defaultPrevented || wantsNative(ev)) return false
  const target = ev.target as Element | null
  const anchor = target?.closest?.('a[href]')
  if (!anchor) return false
  const link = classifyMarkdownLink(anchor.getAttribute('href'))
  if (link.kind === 'route') {
    ev.preventDefault()
    push(link.to)
    return true
  }
  if (link.kind === 'external') {
    ev.preventDefault()
    window.open(link.href, '_blank', 'noopener')
    return true
  }
  return false
}
