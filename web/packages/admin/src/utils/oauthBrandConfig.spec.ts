import { describe, expect, it } from 'vitest'
import { buildConfigProviderRows, BRAND_CODES, LINK_BY_ACCOUNT_CODES } from './oauthBrand'

describe('buildConfigProviderRows', () => {
  it('always lists all brand codes even when none registered', () => {
    const rows = buildConfigProviderRows([])
    expect(rows.map(r => r.code)).toEqual([...BRAND_CODES])
    expect(rows.every(r => !r.registered && !r.enabled)).toBe(true)
  })

  it('marks registered providers and keeps unregistered off', () => {
    const rows = buildConfigProviderRows([
      { code: 'github', displayName: 'GH', enabled: true },
      { code: 'wecom', displayName: '企微', enabled: false },
    ])
    const gh = rows.find(r => r.code === 'github')!
    const wx = rows.find(r => r.code === 'wechat')!
    const wc = rows.find(r => r.code === 'wecom')!
    expect(gh.registered).toBe(true)
    expect(gh.enabled).toBe(true)
    expect(gh.displayName).toBe('GH')
    expect(wx.registered).toBe(false)
    expect(wx.enabled).toBe(false)
    expect(wc.registered).toBe(true)
    expect(wc.enabled).toBe(false)
  })

  it('carries linkByAccount for registered providers and keeps unregistered off', () => {
    const rows = buildConfigProviderRows([
      { code: 'wecom', displayName: '企微', enabled: true, linkByAccount: true },
      { code: 'github', displayName: 'GH', enabled: true },
    ])
    expect(rows.find(r => r.code === 'wecom')!.linkByAccount).toBe(true)
    expect(rows.find(r => r.code === 'github')!.linkByAccount).toBe(false)
    expect(rows.find(r => r.code === 'dingtalk')!.linkByAccount).toBe(false)
  })

  it('offers the link-by-account switch only where the external id is an enterprise account', () => {
    // 企业微信的 userid 由企业统一分配;钉钉 / 微信 / GitHub 的标识对不上本地账号
    expect([...LINK_BY_ACCOUNT_CODES]).toEqual(['wecom'])
  })

  it('appends extra registered codes after brands', () => {
    const rows = buildConfigProviderRows([
      { code: 'oidc-demo', displayName: 'OIDC 演示', enabled: true },
    ])
    expect(rows.some(r => r.code === 'oidc-demo' && r.registered && r.enabled)).toBe(true)
    expect(rows.findIndex(r => r.code === 'oidc-demo')).toBeGreaterThan(
      rows.findIndex(r => r.code === 'qq'),
    )
  })
})
