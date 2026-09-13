// 表格「放大」:把某块区域临时铺满视口,给笔记本等窄屏看宽表用。
//
// 刻意不用 Fullscreen API(VueUse 的 useFullscreen):Naive 的 NSelect / NPopover 把浮层 teleport 到
// body,而原生全屏只渲染被全屏那棵子树,浮层会整个看不见 —— 带下拉、气泡的宽表必然踩中。
// 固定定位遮罩没这个问题:浮层照旧挂在 body 上,z-index 比遮罩高就行(遮罩 50,Naive 浮层 2000+)。
//
// 用法:
//   const { zoomed, toggle } = useTableZoom()
//   <div :class="{ 'table-zoom': zoomed }"> ...表格... </div>
//   <n-button @click="toggle">放大/还原</n-button>
// `.table-zoom` 的样式在 styles/table.css。
import { onScopeDispose, ref, watch } from 'vue'

/**
 * body 滚动锁的引用计数。`document.body.style.overflow` 是全局单例,而一个页面上会有多个
 * useTableZoom 实例,各自无条件置/清会互相踩:先退出的那个会在另一个遮罩仍铺满时
 * 把背景滚动放开,正是这把锁要防的现象。
 *
 * 当前 UI 下同时只可能有一个放大态(遮罩不透明且铺满视口,挡住了其它放大按钮),
 * 所以这个计数眼下是防御性的 —— 但别让后来者依赖那个巧合:一旦出现并排/画中画放大就会踩中。
 */
let bodyLockCount = 0
function lockBodyScroll() {
  if (bodyLockCount === 0) document.body.style.overflow = 'hidden'
  bodyLockCount++
}
function unlockBodyScroll() {
  if (bodyLockCount === 0) return
  bodyLockCount--
  if (bodyLockCount === 0) document.body.style.overflow = ''
}

export function useTableZoom() {
  const zoomed = ref(false)
  /** 本实例是否持有 body 锁:避免重复加锁/重复解锁把计数带偏 */
  let holdsLock = false

  function toggle() {
    zoomed.value = !zoomed.value
  }
  function exit() {
    zoomed.value = false
  }

  function onKeydown(e: KeyboardEvent) {
    // 只在放大态拦 Esc;Naive 的下拉/气泡自己也吃 Esc,已经打开时先关浮层再关放大,交给冒泡顺序即可。
    if (e.key === 'Escape') exit()
  }

  // 放大时锁掉 body 滚动:遮罩是 fixed,底下的页面还能滚会让滚轮行为很怪。
  watch(zoomed, value => {
    if (value && !holdsLock) {
      holdsLock = true
      lockBodyScroll()
      window.addEventListener('keydown', onKeydown)
    } else if (!value && holdsLock) {
      holdsLock = false
      unlockBodyScroll()
      window.removeEventListener('keydown', onKeydown)
    }
  })

  // 组件卸载时(含放大态下直接切路由)务必还锁,否则整站 body 会一直锁着。
  onScopeDispose(() => {
    if (holdsLock) {
      holdsLock = false
      unlockBodyScroll()
    }
    window.removeEventListener('keydown', onKeydown)
  })

  return { zoomed, toggle, exit }
}
