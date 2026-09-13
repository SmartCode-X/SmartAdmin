import { describe, it, expect } from 'vitest'
import '#/locales' // 真 i18n(默认 zh-CN),不 mock
import { ApiError } from '#/api'
import { translateError, translateErrorDetail } from './error'

describe('translateError', () => {
  it('ApiError.msgKey 命中 i18n → 本地化文案', () => {
    const err = new ApiError(40004, 'error.auth.passwordWrong')
    expect(translateError(err)).toBe('账号或密码错误')
  })

  // 后端 AdminException 的 args 就是占位符实参。不传下去的话界面显示的是「每页条数超过上限 {max}」——
  // 最该看到的那个数没了,而这条错误没有那个数就等于没说。
  it('带占位符的文案 → 用 ApiError.args 填实参', () => {
    const err = new ApiError(41005, 'error.request.pageSizeExceeded', { size: 500, max: 100 })
    expect(translateError(err)).toBe('每页条数超过上限 100,请调小分页或使用导出')
  })

  it('无占位符的文案传 args 无害,不需要分支', () => {
    const err = new ApiError(40004, 'error.auth.passwordWrong', { max: 100 })
    expect(translateError(err)).toBe('账号或密码错误')
  })

  it('msgKey 未命中但有 message → message,普通 Error → message', () => {
    const withUnknownKey = new ApiError(
      99999,
      'error.does.not.exist',
      undefined,
      'fallback message text',
    )
    expect(translateError(withUnknownKey)).toBe('fallback message text')
    expect(translateError(new Error('plain error message'))).toBe('plain error message')
  })

  it('未知值 → 兜底键', () => {
    expect(translateError(null)).toBe('操作失败,请稍后重试')
    expect(translateError('random string')).toBe('操作失败,请稍后重试')
  })

  it('数字 ErrorCode(CellError) → 按码查 i18n', () => {
    expect(translateError(46005)).toBe('该单元格为必填项')
    expect(translateError(46010)).toBe('业务键在库中已存在')
    expect(translateError(99999)).toBe('操作失败,请稍后重试')
  })

  // 超时闸抛的是 DOMException,原始 message 是浏览器英文原话("signal timed out"),不能直接弹给用户。
  it('超时 / 主动取消 → 专门文案,不漏出浏览器英文原话', () => {
    const timeout = new DOMException('The operation timed out.', 'TimeoutError')
    const aborted = new DOMException('The operation was aborted.', 'AbortError')
    expect(translateError(timeout)).toBe('请求超时,请检查网络后重试')
    expect(translateError(aborted)).toBe('请求已取消')
  })
})

describe('translateErrorDetail', () => {
  it('无明细(message === msgKey)→ 本地化文案,不是 msgKey 原文', () => {
    // AdminExceptionFilter 无明细时把 Message 置成 msgKey,页面直接取 .message 会把 error.xxx 弹给用户 —— 钉住不能回退到那种写法。
    const err = new ApiError(
      40004,
      'error.auth.passwordWrong',
      undefined,
      'error.auth.passwordWrong',
    )
    expect(translateErrorDetail(err)).toBe('账号或密码错误')
  })

  it('有明细 → 优先显示后端原话', () => {
    const err = new ApiError(40004, 'error.auth.passwordWrong', undefined, '对端返回: Code = 10444')
    expect(translateErrorDetail(err)).toBe('对端返回: Code = 10444')
  })

  it('只有 message 没有 msgKey → 原样返回', () => {
    // ProblemDetails(400/500)一类信封没有 msgKey,原文必须透出,不能被兜底文案盖掉。
    expect(translateErrorDetail(new ApiError(1, undefined, undefined, '明细原文'))).toBe('明细原文')
    expect(translateErrorDetail(new Error('Failed to fetch'))).toBe('Failed to fetch')
  })
})
