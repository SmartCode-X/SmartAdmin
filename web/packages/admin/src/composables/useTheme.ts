import { ref, watch, type Ref } from 'vue'
import { darkTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui'
import { useAppStore } from '#/stores/app'
import { buildThemeOverrides } from '#/theme/naive-theme'
import { derivePrimary } from '#/theme/mix'

/**
 * 主题落地:随 app.isDark / app.accent / app.density / app.grayscale 变化,
 *  1) 打 data-theme / data-density 到 <html>(裸 CSS 与 tokens 跟着翻);
 *  2) 把 accent 派生的 --color-primary* 写到 <html>(令消费 tokens 的裸 CSS 换色);
 *  3) 重建 Naive themeOverrides(新对象触发 n-config-provider 重渲染)。
 * 返回类型显式写出:推断出的 Naive 主题类型展开后超出 d.ts 序列化上限,包的类型声明会不完整。
 */
export function useTheme(): {
  overrides: Ref<GlobalThemeOverrides>
  naiveTheme: Ref<GlobalTheme | null>
} {
  const app = useAppStore()
  const overrides = ref<GlobalThemeOverrides>({}) as Ref<GlobalThemeOverrides>
  const naiveTheme = ref<GlobalTheme | null>(null) as Ref<GlobalTheme | null>

  // 主题切换过渡:短暂给 <html> 加 theme-switching,让颜色平滑过渡且不盖整屏淡化层,文字不会短暂发糊。
  let transitionTimer: ReturnType<typeof setTimeout> | null = null

  function beginThemeTransition() {
    const el = document.documentElement
    el.classList.add('theme-switching')
    if (transitionTimer) clearTimeout(transitionTimer)
    transitionTimer = setTimeout(() => el.classList.remove('theme-switching'), 280)
  }

  function applyPrimaryVars() {
    const el = document.documentElement
    // 容器色要在 data-theme 打上之后读(apply() 里的顺序),否则暗色首帧会拿到亮色容器。
    const container =
      getComputedStyle(el).getPropertyValue('--color-bg-container').trim() || undefined
    const p = derivePrimary(app.accent, app.isDark, container)
    el.style.setProperty('--color-primary', p.primary)
    el.style.setProperty('--color-primary-hover', p.hover)
    el.style.setProperty('--color-primary-pressed', p.pressed)
    el.style.setProperty('--color-primary-light', p.light)
  }

  function apply() {
    const nextDark = app.isDark
    beginThemeTransition() // 主题切换瞬间启用颜色过渡,不做整屏淡化
    const el = document.documentElement
    el.setAttribute('data-theme', nextDark ? 'dark' : '')
    el.setAttribute('data-density', app.density)
    el.toggleAttribute('data-gray', app.grayscale) // 灰阶滤镜(styles/index.css)
    applyPrimaryVars()
    naiveTheme.value = app.isDark ? darkTheme : null
    overrides.value = buildThemeOverrides({ dark: app.isDark, accent: app.accent })
  }

  watch([() => app.isDark, () => app.accent, () => app.density, () => app.grayscale], apply, {
    immediate: true,
  })

  return { overrides, naiveTheme }
}
