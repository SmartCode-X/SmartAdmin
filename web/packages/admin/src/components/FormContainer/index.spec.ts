import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import { createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FormContainer from './index.vue'

// n-modal 的卡片 teleport 到 body,所以断言全在 document.body 上找 —— 这也正是全屏样式
// 不能写 scoped 的原因(scoped 的 data-v 属性到不了 teleport 出去的那棵子树)。
let app: App<Element> | undefined

function mount(props: Record<string, unknown>) {
  const host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp(() => h(FormContainer, { title: '表单', show: true, variant: 'modal', ...props }))
  app.use(createPinia())
  app.use(
    createI18n({
      legacy: false,
      locale: 'zh-CN',
      messages: { 'zh-CN': { common: { cancel: '取消', confirm: '确定' } } },
    }),
  )
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.innerHTML = ''
})

describe('FormContainer 全屏', () => {
  it('默认不全屏 —— 没传 prop 的既有页面不该因为换台设备就换形态', async () => {
    mount({})
    await nextTick()
    expect(document.body.querySelector('.smart-form--fullscreen')).toBeNull()
  })

  it('fullscreen 为 true 时卡片带全屏类', async () => {
    mount({ fullscreen: true })
    await nextTick()
    expect(document.body.querySelector('.smart-form--fullscreen')).not.toBeNull()
  })

  // 'auto' 才跟着屏幕走;happy-dom 默认视口 1024×768 不算紧凑,所以仍是常规卡片。
  it("fullscreen 为 'auto' 时按紧凑屏判定,宽屏下不全屏", async () => {
    mount({ fullscreen: 'auto' })
    await nextTick()
    expect(document.body.querySelector('.smart-form--fullscreen')).toBeNull()
  })
})
