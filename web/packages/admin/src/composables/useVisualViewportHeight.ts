// 把「真正看得见的高度」写进 `--vvh`(px),供需要避开软键盘的页面用:`height: var(--vvh, 100dvh)`。
//
// 为什么不是 100dvh 就够:`dvh` 跟的是**布局视口**,软键盘弹起时它不变 —— 平板上点一个靠底部的输入框,
// 键盘盖住半屏,页面并不知道,固定在底部的表单按钮就压在键盘底下点不到。
// `visualViewport.height` 是唯一会随键盘缩小的量。
//
// 全局调一次即可(布局壳已调),不覆盖 `100dvh` 的既有行为:变量只是供页面按需消费。
import { onScopeDispose, ref, type Ref } from 'vue'

const CSS_VAR = '--vvh'

/** 返回当前可视高度(px);同时持续写进 documentElement 的 `--vvh`。 */
export function useVisualViewportHeight(): Ref<number> {
  const height = ref(0)

  // 老浏览器 / 测试环境没有 visualViewport:退回 innerHeight,变量照写,消费方无需分支。
  const viewport = typeof window !== 'undefined' ? window.visualViewport : undefined

  function update() {
    const h = Math.round(viewport?.height ?? window.innerHeight ?? 0)
    if (h <= 0 || h === height.value) return
    height.value = h
    document.documentElement.style.setProperty(CSS_VAR, `${h}px`)
  }

  update()

  if (viewport) {
    viewport.addEventListener('resize', update)
    // 键盘弹起时 iOS 是「视口不变、往上顶」,只有 scroll 事件反映得出来。
    viewport.addEventListener('scroll', update)
  } else {
    window.addEventListener('resize', update)
  }

  onScopeDispose(() => {
    if (viewport) {
      viewport.removeEventListener('resize', update)
      viewport.removeEventListener('scroll', update)
    } else {
      window.removeEventListener('resize', update)
    }
    document.documentElement.style.removeProperty(CSS_VAR)
  })

  return height
}
