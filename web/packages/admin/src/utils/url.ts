/**
 * 是否 http(s) 绝对 URL。外链/iframe 菜单的约定判据:
 * - 外链菜单:节点 path 为 URL(component 空)→ 点击 window.open,不建路由。
 * - 内嵌 iframe 菜单:节点 component 为 URL(path 为内部路径)→ 注册通用 iframe 视图,URL 进 meta.iframeSrc。
 */
export function isHttpUrl(s: string | undefined | null): boolean {
  return !!s && /^https?:\/\//i.test(s)
}

/** 去掉尾部连续的 `/`（中间的不动）。从尾部线性扫描，不用正则。 */
export function trimTrailingSlashes(s: string): string {
  let end = s.length
  while (end > 0 && s.charCodeAt(end - 1) === 47) end--
  return s.slice(0, end)
}

/** 整屏页路由前缀：这类路由注册在布局壳之外，占满整个窗口（车间大屏看板等）。 */
export const FULLSCREEN_PATH_PREFIX = '/fullscreen/'

/**
 * 是否「站内整屏页」菜单：节点 path 以 {@link FULLSCREEN_PATH_PREFIX} 开头、component 空。
 *
 * 与外链菜单同样走 `window.open` 新标签打开，但目标是站内静态路由而不是外部 URL——
 * 大屏看板挂在墙上，必须整屏无侧栏无标签栏，塞进布局壳里就没意义了；
 * 又不能像外链那样写死 `http://host/...`，那会把部署域名钉死在菜单数据里。
 */
export function isFullscreenPath(s: string | undefined | null): boolean {
  return !!s && s.startsWith(FULLSCREEN_PATH_PREFIX)
}
