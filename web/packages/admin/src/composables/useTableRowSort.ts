// n-data-table 的行拖拽排序:sortablejs 的挂载/重建/销毁与重排回调都收在这里。
//
// 为什么不用原生 HTML5 拖放(<tr draggable> + dragstart/drop):那个半透明拖影由浏览器绘制,
// 能跟着鼠标飘到表格外、弹窗外甚至窗口外,CSS 一点都管不到。这里用 sortablejs 的
// forceFallback + 隐藏浮层(样式里 .row-fallback { display: none }),拖拽全靠行本身移动来表达,
// 视觉上永远出不了表体;让位动画与自动滚动交给 sortablejs。
import { nextTick, onBeforeUnmount, type Ref } from 'vue'
import type Sortable from 'sortablejs'

export interface TableRowSortOptions<T> {
  /**
   * 表格所在的容器元素,从中找 tbody;拖拽期间它会被打上 .is-dragging。
   * 该元素要套全局类 <code>.row-sort</code>(styles/table.css)才有配套外观。
   */
  container: Ref<HTMLElement | null>
  /** 被排序的数组,原地重排(reactive 数组直接传函数返回值即可) */
  rows: () => T[]
  /** 为真时不可拖(只读态、详情态) */
  disabled: () => boolean
  /** 重排完成:落位的行 + 原始/目标下标 */
  onSorted?: (row: T, from: number, to: number) => void
}

/** 拖拽手柄的类名,列渲染和样式两边都按它对齐 */
export const DRAG_HANDLE_CLASS = 'drag-handle'

/** tbody 还没渲染出来时的重试次数与间隔:弹窗有开场动画,首帧往往取不到表体 */
const RETRY_LIMIT = 10
const RETRY_DELAY = 80

export function useTableRowSort<T>(options: TableRowSortOptions<T>) {
  let sortable: Sortable | null = null
  let boundTbody: HTMLElement | null = null
  let retryTimer: ReturnType<typeof setTimeout> | undefined

  function destroy() {
    clearTimeout(retryTimer)
    sortable?.destroy()
    sortable = null
    boundTbody = null
  }

  /**
   * 重新绑定拖拽。表格换了一个 tbody(弹窗重开、只读态切换)才需要真的重建,
   * 行的增删不动 tbody 元素本身,重复调用是廉价的。
   */
  async function sync(attempt = 0) {
    if (options.disabled()) {
      destroy()
      return
    }
    await nextTick()
    const tbody = options.container.value?.querySelector<HTMLElement>('.n-data-table-tbody')
    if (!tbody) {
      destroy()
      // 弹窗开场动画期间表体还没挂上,过一会儿再试,别把拖拽丢了
      if (attempt < RETRY_LIMIT) {
        retryTimer = setTimeout(() => void sync(attempt + 1), RETRY_DELAY)
      }
      return
    }
    if (tbody === boundTbody) return
    clearTimeout(retryTimer)

    destroy()
    const { default: SortableCtor } = await import('sortablejs')
    sortable = SortableCtor.create(tbody, {
      handle: `.${DRAG_HANDLE_CLASS}`,
      draggable: '.n-data-table-tr',
      animation: 180,
      easing: 'cubic-bezier(0.22, 0.61, 0.36, 1)',
      chosenClass: 'row-chosen',
      ghostClass: 'row-ghost',
      fallbackClass: 'row-fallback',
      // 自绘拖拽层(而不是浏览器的原生拖影),配合样式里的隐藏才能把拖拽锁在表内
      forceFallback: true,
      // 手柄上的轻微抖动不该被当成拖拽,否则点一下就误触发
      fallbackTolerance: 4,
      scroll: true,
      forceAutoScrollFallback: true,
      scrollSensitivity: 64,
      scrollSpeed: 12,
      onStart: () => options.container.value?.classList.add('is-dragging'),
      onEnd: event => {
        options.container.value?.classList.remove('is-dragging')
        const { oldIndex: from, newIndex: to } = event
        if (from == null || to == null || from === to) return
        // sortablejs 已经把 DOM 挪好了,这里同步数据;表格带 row-key,Vue 按键重排不会串行
        const rows = options.rows()
        const [row] = rows.splice(from, 1)
        if (!row) return
        rows.splice(to, 0, row)
        options.onSorted?.(row, from, to)
      },
    })
    boundTbody = tbody
  }

  onBeforeUnmount(destroy)

  return { sync, destroy }
}
