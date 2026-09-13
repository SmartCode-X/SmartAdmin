// 发版后旧 chunk 404 的兜底。
//
// 用户停在旧的 index.html,而懒加载的 JS 文件名带 hash,新版一上线旧文件就没了 ——
// 再点任何一个还没加载过的页面就是白屏。唯一可靠的修法是重载页面拿新 index.html。
// 但如果新版本身也加载不了(CDN 没同步完、断网、被网关拦),无条件重载会变成刷新死循环,
// 所以用 sessionStorage 记一次:每个标签页只自动救一次,之后交给错误边界让用户自己决定。

const RELOADED_FLAG = 'smart:chunk-reloaded'

// 各浏览器对「动态导入失败」的文案不统一,统一按关键词认。
const CHUNK_ERROR_RE =
  /dynamically imported module|Importing a module script failed|ChunkLoadError|Loading chunk \d+ failed|css chunk/i

/** 是不是「旧 chunk 没了」这类动态导入失败。 */
export function isChunkLoadError(err: unknown): boolean {
  if (!err) return false
  const text = err instanceof Error ? `${err.name}: ${err.message}` : String(err)
  return CHUNK_ERROR_RE.test(text)
}

/**
 * 本标签页还没自动重载过就重载,返回是否真的触发了。
 * sessionStorage 读写在隐私模式下会抛 —— 那时宁可不重载,也不能冒死循环的险。
 */
export function reloadOnce(): boolean {
  try {
    if (sessionStorage.getItem(RELOADED_FLAG)) return false
    sessionStorage.setItem(RELOADED_FLAG, '1')
  } catch {
    return false
  }
  window.location.reload()
  return true
}

/** 是 chunk 失效且本页还没自救过 → 重载。返回 true 表示调用方不必再弹错误。 */
export function reloadOnChunkError(err: unknown): boolean {
  return isChunkLoadError(err) && reloadOnce()
}
