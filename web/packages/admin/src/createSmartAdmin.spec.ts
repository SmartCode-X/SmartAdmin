import { iconLoaded } from '@iconify/vue'
import { describe, expect, it } from 'vitest'
import { createSmartAdmin } from '#/createSmartAdmin'

const set = (name: string) => ({
  prefix: 'ph',
  icons: { [name]: { body: '<path d="M0 0h8v8H0z"/>' } },
  width: 256,
  height: 256,
})

describe('createSmartAdmin', () => {
  it('收集插件与应用两层的 iconSets,启动时同步注册', () => {
    expect(iconLoaded('ph:truck-duotone')).toBe(false)
    expect(iconLoaded('ph:anchor')).toBe(false)
    createSmartAdmin({ plugins: [{ iconSets: [set('truck-duotone')] }], iconSets: [set('anchor')] })
    expect(iconLoaded('ph:truck-duotone')).toBe(true)
    expect(iconLoaded('ph:anchor')).toBe(true)
  })
})
