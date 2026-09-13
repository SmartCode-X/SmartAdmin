import { describe, it, expect } from 'vitest'
import { classifyNoticeAction, isSiteRelativeUrl, noticeActionLabel } from './noticeActions'

describe('isSiteRelativeUrl', () => {
  it('单个 / 开头才算站内路径', () => {
    expect(isSiteRelativeUrl('/system/job-log?jobId=1')).toBe(true)
    expect(isSiteRelativeUrl('/api/v1/sys/job/1/run')).toBe(true)
    expect(isSiteRelativeUrl('/')).toBe(true)
  })
  it('协议相对、外链、空白与反斜杠一律拒绝', () => {
    expect(isSiteRelativeUrl('//evil.com')).toBe(false)
    expect(isSiteRelativeUrl('/\\evil.com')).toBe(false)
    expect(isSiteRelativeUrl('https://evil.com')).toBe(false)
    expect(isSiteRelativeUrl('/a b')).toBe(false)
    expect(isSiteRelativeUrl('/a\\b')).toBe(false)
    expect(isSiteRelativeUrl('')).toBe(false)
    expect(isSiteRelativeUrl(null)).toBe(false)
  })
})

describe('classifyNoticeAction', () => {
  it('/api/ 开头是接口调用,其它站内路径是页面跳转', () => {
    expect(classifyNoticeAction({ url: '/api/v1/sys/job/1/run' })).toBe('api')
    expect(classifyNoticeAction({ url: '/api' })).toBe('api')
    expect(classifyNoticeAction({ url: '/apix/y' })).toBe('route')
    expect(classifyNoticeAction({ url: '/system/job-log?jobId=1' })).toBe('route')
  })
  it('不合法的 url 归为 invalid', () => {
    expect(classifyNoticeAction({ url: '//evil.com' })).toBe('invalid')
    expect(classifyNoticeAction({ url: 'http://x' })).toBe('invalid')
    expect(classifyNoticeAction({ url: '' })).toBe('invalid')
  })
})

describe('noticeActionLabel', () => {
  const dict: Record<string, string> = { 'job.retry': '重试' }
  const t = (k: string) => dict[k] ?? k
  const te = (k: string) => k in dict
  it('含 . 且能查到的按 i18n key 翻译,否则原样', () => {
    expect(noticeActionLabel('job.retry', t, te)).toBe('重试')
    expect(noticeActionLabel('job.missing', t, te)).toBe('job.missing')
    expect(noticeActionLabel('查看日志', t, te)).toBe('查看日志')
  })
})
