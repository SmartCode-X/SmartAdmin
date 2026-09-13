import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  addRoute: vi.fn(),
  hasRoute: vi.fn(() => false),
  removeRoute: vi.fn(),
  registerDynamic: vi.fn(),
  namedPage: vi.fn((name: string) => ({ name })),
  detailViews: vi.fn(() => [] as Array<[string, () => Promise<unknown>]>),
}))

vi.mock('#/router', () => ({
  router: { addRoute: mocks.addRoute, hasRoute: mocks.hasRoute, removeRoute: mocks.removeRoute },
  registerDynamic: mocks.registerDynamic,
}))
vi.mock('#/router/namedPage', () => ({ namedPage: mocks.namedPage }))
vi.mock('#/router/viewRegistry', () => ({ detailViews: mocks.detailViews }))

import { registerDetailRoutes } from './detailRoutes'

const loader = () => Promise.resolve({ default: {} })

describe('registerDetailRoutes', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasRoute.mockReturnValue(false)
    mocks.detailViews.mockReturnValue([])
  })

  it('把 detail.vue 约定登记成 layout 下的路由', () => {
    mocks.detailViews.mockReturnValue([['demo/order/detail', loader]])

    registerDetailRoutes()

    expect(mocks.addRoute).toHaveBeenCalledWith(
      'layout',
      expect.objectContaining({
        path: '/demo/order/:id/detail',
        name: 'detail-demo-order',
        meta: { title: 'common.detail', noCache: true },
      }),
    )
    expect(mocks.registerDynamic).toHaveBeenCalledWith('detail-demo-order')
  })

  it('已存在同名路由时先 removeRoute 再 addRoute(幂等)', () => {
    mocks.detailViews.mockReturnValue([['demo/order/detail', loader]])
    mocks.hasRoute.mockReturnValue(true)

    registerDetailRoutes()

    expect(mocks.removeRoute).toHaveBeenCalledWith('detail-demo-order')
    expect(mocks.addRoute).toHaveBeenCalled()
  })
})
