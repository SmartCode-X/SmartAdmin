// 通知动作(NoticeAction)的纯逻辑:url 分类与展示文案。服务端只存不解释,这里决定"点了怎么办"。
import type { NoticeAction } from '#/types/api'

export type NoticeActionKind =
  /** /api/ 开头:按 method 请求该接口 */
  | 'api'
  /** 其它单个 / 开头的站内路径:router.push 跳过去(method 忽略) */
  | 'route'
  /** 不是单个 / 开头(含 //host、/\host 这类协议相对 URL):不执行 */
  | 'invalid'

/** 与后端 NoticeService.IsSiteRelativeUrl 同一条规则:单个 `/` 开头、无空白与反斜杠。 */
export function isSiteRelativeUrl(url: string | null | undefined): boolean {
  if (!url || url[0] !== '/') return false
  if (url.length > 1 && (url[1] === '/' || url[1] === '\\')) return false
  return !/[\s\\]/.test(url)
}

export function classifyNoticeAction(action: Pick<NoticeAction, 'url'>): NoticeActionKind {
  const url = (action.url ?? '').trim()
  if (!isSiteRelativeUrl(url)) return 'invalid'
  return url === '/api' || url.startsWith('/api/') ? 'api' : 'route'
}

/** 按钮文案:含 `.` 视为 i18n key(与菜单标题同一条约定),查不到就原样显示。 */
export function noticeActionLabel(
  label: string,
  t: (key: string) => string,
  te: (key: string) => boolean,
): string {
  return label.includes('.') && te(label) ? t(label) : label
}
