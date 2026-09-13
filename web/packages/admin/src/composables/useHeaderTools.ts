// 顶栏工具区登记表:扩展模块把自己的入口(图标按钮或整块组件)放进顶栏。
// 不做成 AppHeader 上的具名插槽:AppHeader 只有 layouts/default.vue 一处使用,走插槽就得连 default.vue
// 一起改,而两者都在包里,应用改不到。登记表让扩展代码留在应用自己的文件里。
import { computed, shallowReactive, type Component, type ComputedRef } from 'vue'

interface HeaderToolBase {
  /** 唯一键,重复登记以最后一次为准 */
  key: string
  /** 排序,越小越靠左;内置项(铃铛、用户下拉)恒在登记项右侧 */
  order?: number
  /** 窄屏(<768px)是否保留。默认隐藏:顶栏挤不下时右端会被裁,最右的登出入口优先 */
  keepOnMobile?: boolean
}

/** 图标模式:一个圆形 quaternary 按钮 + 悬浮提示,适合"点了就跳/就开弹窗"的入口。 */
export interface HeaderToolIcon extends HeaderToolBase {
  /** iconify 名(Phosphor 的 calendar-dots 之类)。子集外的名字由 AppIcon 懒加载兜底 */
  icon: string
  /** 悬浮提示与 aria-label(自行翻译好) */
  label: string
  onClick: () => void
  component?: never
}

/** 组件模式:整块交给你自己的组件渲染(自带角标 / 弹层的入口,内置铃铛就是这个形状)。 */
export interface HeaderToolComponent extends HeaderToolBase {
  component: Component
  icon?: never
  label?: never
  onClick?: never
}

export type HeaderTool = HeaderToolIcon | HeaderToolComponent

const tools = shallowReactive(new Map<string, HeaderTool>())

/** 往顶栏工具区放一项,返回注销函数。 */
export function registerHeaderTool(tool: HeaderTool): () => void {
  tools.set(tool.key, tool)
  return () => {
    if (tools.get(tool.key) === tool) tools.delete(tool.key)
  }
}

/** 已登记的工具项,按 order 升序(同 order 保持登记先后)。AppHeader 渲染用。 */
export function useHeaderTools(): ComputedRef<HeaderTool[]> {
  return computed(() => [...tools.values()].toSorted((a, b) => (a.order ?? 0) - (b.order ?? 0)))
}
