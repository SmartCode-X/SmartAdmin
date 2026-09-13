// 紧凑屏判定:视口小到「桌面密度那一套(560px 卡片、并排两栏、悬停操作)不再合适」的时候。
//
// 判据是视口尺寸而不是 UA:平板既可能横屏当笔记本用,也可能竖屏当大手机用,同一台设备两种形态,
// 认 UA 只会两边都判错。软键盘弹起把可视区压扁的情况同理 —— 那时也该按紧凑处理。
import { computed, type ComputedRef } from 'vue'
import { useWindowSize } from '@vueuse/core'

/** 宽度阈值:iPad 竖屏是 768 / 834,横屏 1024 起;手机远小于此。 */
export const COMPACT_MAX_WIDTH = 900
/** 高度阈值:1280×600 这类矮屏笔记本,以及横屏平板弹出软键盘后剩下的高度。 */
export const COMPACT_MAX_HEIGHT = 640

/** 纯判定,便于单测与非响应式场景直接调用。 */
export function isCompactSize(
  width: number,
  height: number,
  maxWidth = COMPACT_MAX_WIDTH,
  maxHeight = COMPACT_MAX_HEIGHT,
): boolean {
  return width <= maxWidth || height <= maxHeight
}

/** 响应式紧凑屏标记。阈值可按页覆盖(如某张宽表要更早切紧凑)。 */
export function useCompactScreen(opts?: {
  maxWidth?: number
  maxHeight?: number
}): ComputedRef<boolean> {
  const { width, height } = useWindowSize()
  return computed(() => isCompactSize(width.value, height.value, opts?.maxWidth, opts?.maxHeight))
}
