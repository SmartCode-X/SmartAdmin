import type { GlobalThemeOverrides } from 'naive-ui'
import { derivePrimary, mix } from './mix'

// 读取当前主题下的 token 值(getComputedStyle 同步反映最新的 data-theme)。
const v = (name: string): string =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim()

/** 语义色 hover/pressed/suppl 统一派生。主色不在此列——它走 `mix.ts` 的 `derivePrimary`(与写 CSS 变量的那处共用)。 */
function semantic(base: string) {
  return {
    base,
    hover: mix(base, '#FFFFFF', 0.16),
    pressed: mix(base, '#000000', 0.18),
    suppl: mix(base, '#FFFFFF', 0.16),
  }
}

/**
 * tokens.css → Naive UI GlobalThemeOverrides(DESIGN.md 映射表)。
 * 主色由 accent 按派生规则算;其余颜色/度量从当前 CSS 变量读取。
 */
export function buildThemeOverrides(opts: { dark: boolean; accent: string }): GlobalThemeOverrides {
  const p = derivePrimary(opts.accent, opts.dark)
  const ok = semantic(v('--color-success'))
  const warn = semantic(v('--color-warning'))
  const err = semantic(v('--color-danger'))
  const info = semantic(v('--color-info'))
  return {
    common: {
      primaryColor: p.primary,
      primaryColorHover: p.hover,
      primaryColorPressed: p.pressed,
      primaryColorSuppl: p.hover,

      successColor: ok.base,
      successColorHover: ok.hover,
      successColorPressed: ok.pressed,
      successColorSuppl: ok.suppl,
      warningColor: warn.base,
      warningColorHover: warn.hover,
      warningColorPressed: warn.pressed,
      warningColorSuppl: warn.suppl,
      errorColor: err.base,
      errorColorHover: err.hover,
      errorColorPressed: err.pressed,
      errorColorSuppl: err.suppl,
      infoColor: info.base,
      infoColorHover: info.hover,
      infoColorPressed: info.pressed,
      infoColorSuppl: info.suppl,

      bodyColor: v('--color-bg-body'),
      cardColor: v('--color-bg-container'),
      tableColor: v('--color-bg-container'),
      modalColor: v('--color-bg-elevated'),
      popoverColor: v('--color-bg-elevated'),

      textColorBase: v('--color-text-primary'),
      textColor1: v('--color-text-primary'),
      textColor2: v('--color-text-secondary'),
      textColor3: v('--color-text-tertiary'),
      placeholderColor: v('--color-text-tertiary'),
      textColorDisabled: v('--color-text-disabled'),

      borderColor: v('--color-border'),
      dividerColor: v('--color-border'),
      actionColor: v('--color-fill'),
      // 两者同色会让禁用框和可填框长一个样,操作员分不清哪里能输入,必须走不同令牌。
      // 这两个 common 变量流向 Input 与 InternalSelection,
      // 即 NInput / NInputNumber / NSelect / NDatePicker / NCascader 等全部输入类组件。
      inputColor: v('--color-bg-input'),
      inputColorDisabled: v('--color-fill-disabled'),
      tableHeaderColor: v('--color-fill'),
      hoverColor: v('--color-fill-hover'),

      borderRadius: v('--radius-md'),
      borderRadiusSmall: v('--radius-sm'),
      fontSize: v('--font-size-base'),
      fontSizeMedium: v('--font-size-base'),
      fontFamily: v('--font-family-base'),
      fontFamilyMono: v('--font-family-mono'),

      boxShadow1: v('--shadow-1'),
      boxShadow2: v('--shadow-2'),
      boxShadow3: v('--shadow-3'),
    },
    // 卡片圆角走 lg(12);常规控件走 common.borderRadius(md=10)。
    Card: { borderRadius: v('--radius-lg') },
    // 表头质感(corporate 风):次级色 + 半粗字重,与数据行拉开层次。
    DataTable: {
      thTextColor: v('--color-text-secondary'),
      thFontWeight: '600',
      // 表头/斑马纹统一贴近深色背景,不沿用 Naive 默认浅灰,避免表格发白。
      tdColor: v('--color-bg-container'),
      tdColorStriped: v('--color-fill'),
      tdColorHover: v('--color-fill-hover'),
      // 表头底色:对应 Naive 的 tableHeaderColor(官方浅色档是 rgb(250,250,252)),
      // 这里换成项目令牌 —— 官方那档浅灰压在本项目的暗色背景上会发白。
      // hover 与排序态也一并覆盖:Naive 是拿它自己那档浅灰算出来的,不跟着 thColor 走,
      // 不覆盖的话鼠标划过表头会跳成另一个色系。
      thColor: v('--color-fill'),
      thColorHover: v('--color-fill-hover'),
      thColorSorting: v('--color-fill-hover'),
    },
  }
}
