// 原生全屏开关。自己写而不用 VueUse 的 `useFullscreen`,只为一件事:**iPadOS 上它把 isSupported 判成 false**。
//
// iPadOS 的 Safari 默认「请求桌面网站」,UA 看着就是 macOS Safari,于是不会走移动端分支;
// 但它的 Element 上只有带 `webkit` 前缀的 `webkitRequestFullscreen`,没有标准名。VueUse 只探标准名,
// 结论就是「不支持」—— 顶栏的全屏按钮在一台明明能全屏的设备上永远是灰的。
//
// 真不支持的设备(iPhone Safari:只有 video 能全屏)照旧回 false,调用方据此把按钮藏掉,
// 而不是留一个点了没反应的按钮。
import { onScopeDispose, ref, type Ref } from 'vue'

type FullscreenElement = Element & {
  requestFullscreen?: () => Promise<void>
  webkitRequestFullscreen?: () => Promise<void> | void
  webkitRequestFullScreen?: () => Promise<void> | void
  mozRequestFullScreen?: () => Promise<void> | void
  msRequestFullscreen?: () => Promise<void> | void
}

type FullscreenDocument = Document & {
  exitFullscreen?: () => Promise<void>
  webkitExitFullscreen?: () => Promise<void> | void
  webkitCancelFullScreen?: () => Promise<void> | void
  mozCancelFullScreen?: () => Promise<void> | void
  msExitFullscreen?: () => Promise<void> | void
  fullscreenElement?: Element | null
  webkitFullscreenElement?: Element | null
  webkitCurrentFullScreenElement?: Element | null
  mozFullScreenElement?: Element | null
  msFullscreenElement?: Element | null
}

const REQUEST_KEYS = [
  'requestFullscreen',
  'webkitRequestFullscreen',
  'webkitRequestFullScreen',
  'mozRequestFullScreen',
  'msRequestFullscreen',
] as const

const EXIT_KEYS = [
  'exitFullscreen',
  'webkitExitFullscreen',
  'webkitCancelFullScreen',
  'mozCancelFullScreen',
  'msExitFullscreen',
] as const

const ELEMENT_KEYS = [
  'fullscreenElement',
  'webkitFullscreenElement',
  'webkitCurrentFullScreenElement',
  'mozFullScreenElement',
  'msFullscreenElement',
] as const

// 前缀实现派发的是各自的事件名,少监听一个就会出现「已全屏但图标没变」。
const CHANGE_EVENTS = [
  'fullscreenchange',
  'webkitfullscreenchange',
  'mozfullscreenchange',
  'MSFullscreenChange',
] as const

function pickMethod<T extends string>(host: object, keys: readonly T[]): T | undefined {
  return keys.find(k => typeof (host as Record<string, unknown>)[k] === 'function')
}

/** 当前是否处于全屏(把带前缀的 fullscreenElement 一并认全)。 */
function currentFullscreenElement(doc: FullscreenDocument): Element | null {
  for (const key of ELEMENT_KEYS) {
    const el = doc[key]
    if (el) return el
  }
  return null
}

export interface FullscreenToggle {
  /** 本设备是否真能全屏。false 时调用方应隐藏按钮,而不是留个点了没反应的。 */
  isSupported: boolean
  isFullscreen: Ref<boolean>
  enter: () => Promise<void>
  exit: () => Promise<void>
  toggle: () => Promise<void>
}

/** @param target 要全屏的元素,默认整个文档。 */
export function useFullscreenToggle(target?: () => Element | null | undefined): FullscreenToggle {
  const doc = document as FullscreenDocument
  const isFullscreen = ref(false)

  const el = () => (target?.() ?? document.documentElement) as FullscreenElement
  const requestKey = pickMethod(el(), REQUEST_KEYS)
  const exitKey = pickMethod(doc, EXIT_KEYS)
  const isSupported = !!requestKey && !!exitKey

  function sync() {
    isFullscreen.value = !!currentFullscreenElement(doc)
  }
  sync()

  async function enter() {
    if (!requestKey || isFullscreen.value) return
    await el()[requestKey]?.()
    sync() // 前缀实现未必派发事件,进出后主动对一次状态
  }

  async function exit() {
    if (!exitKey || !isFullscreen.value) return
    await doc[exitKey]?.()
    sync()
  }

  async function toggle() {
    await (isFullscreen.value ? exit() : enter())
  }

  if (isSupported) {
    for (const e of CHANGE_EVENTS) document.addEventListener(e, sync)
    onScopeDispose(() => {
      for (const e of CHANGE_EVENTS) document.removeEventListener(e, sync)
    })
  }

  return { isSupported, isFullscreen, enter, exit, toggle }
}
