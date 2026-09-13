import { describe, it, expect } from 'vitest'
import { isHttpUrl, trimTrailingSlashes } from './url'

describe('trimTrailingSlashes', () => {
  it('只去掉尾部连续的 /,中间的 // 不动', () => {
    expect(trimTrailingSlashes('http://a/api///')).toBe('http://a/api')
    expect(trimTrailingSlashes('http://a//b/')).toBe('http://a//b')
    expect(trimTrailingSlashes('http://a/api')).toBe('http://a/api')
    expect(trimTrailingSlashes('/api')).toBe('/api')
  })
  it('空串与全是 / 的串 → 空串', () => {
    expect(trimTrailingSlashes('')).toBe('')
    expect(trimTrailingSlashes('/')).toBe('')
    expect(trimTrailingSlashes('///')).toBe('')
  })
  it('一长串 / 后跟别的字符 → 原样返回', () => {
    const long = '/'.repeat(100_000) + 'x'
    expect(trimTrailingSlashes(long)).toBe(long)
  })
})

describe('isHttpUrl', () => {
  it('识别 http(s) 绝对 URL', () => {
    expect(isHttpUrl('https://example.com')).toBe(true)
    expect(isHttpUrl('http://a.b/c?d=1')).toBe(true)
    expect(isHttpUrl('HTTPS://X.Y')).toBe(true)
  })
  it('内部路径 / 空值不算 URL', () => {
    expect(isHttpUrl('/system/user')).toBe(false)
    expect(isHttpUrl('system/user/index')).toBe(false)
    expect(isHttpUrl('')).toBe(false)
    expect(isHttpUrl(undefined)).toBe(false)
    expect(isHttpUrl(null)).toBe(false)
    // 协议相对/其他协议不接受(iframe/外链需明确 http(s))
    expect(isHttpUrl('//example.com')).toBe(false)
    expect(isHttpUrl('ftp://x')).toBe(false)
  })
})
