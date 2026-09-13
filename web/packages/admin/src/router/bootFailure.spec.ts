import { describe, expect, it } from 'vitest'
import { ApiError } from '#/api'
import { isSessionDead } from './bootFailure'

describe('isSessionDead', () => {
  it('401 与令牌失效业务码 → 会话已死', () => {
    expect(isSessionDead(new ApiError(401))).toBe(true)
    expect(isSessionDead(new ApiError(40006))).toBe(true)
    expect(isSessionDead(new ApiError(40007))).toBe(true)
  })

  // 这几类不是会话问题,不该清会话踢回登录页 —— 重登还是会撞上同一个问题。
  it('网络错 / 5xx / 限流 / 其它业务码 → 不动会话', () => {
    expect(isSessionDead(new TypeError('Failed to fetch'))).toBe(false)
    expect(isSessionDead(new ApiError(500))).toBe(false)
    expect(isSessionDead(new ApiError(502))).toBe(false)
    expect(isSessionDead(new ApiError(40008))).toBe(false) // TooManyRequests
    expect(isSessionDead(undefined)).toBe(false)
  })
})
