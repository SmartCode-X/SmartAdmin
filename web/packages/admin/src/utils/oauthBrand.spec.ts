import { describe, expect, it } from 'vitest'
import {
  claimWeComAutoLogin,
  isIconifyName,
  mergeBindingRows,
  resolveProviderIcon,
  splitLoginProviders,
  SSO_VISIBLE_MAX,
} from './oauthBrand'

const codes = (n: number) =>
  Array.from({ length: n }, (_, i) => ({ code: `p${i}`, displayName: `P${i}` }))

function memoryStorage() {
  const data = new Map<string, string>()
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
  }
}

describe('splitLoginProviders', () => {
  it('0 providers → empty', () => {
    const r = splitLoginProviders([])
    expect(r.visible).toEqual([])
    expect(r.overflow).toEqual([])
  })

  it('1 and 4 → all visible, no overflow; preserves order', () => {
    for (const n of [1, 4]) {
      const list = codes(n)
      const r = splitLoginProviders(list)
      expect(r.visible.map(x => x.code)).toEqual(list.map(x => x.code))
      expect(r.overflow).toEqual([])
    }
  })

  it('5+ → first N visible, rest overflow; no reorder', () => {
    const list = codes(6)
    const r = splitLoginProviders(list)
    expect(r.visible.map(x => x.code)).toEqual(['p0', 'p1', 'p2', 'p3'])
    expect(r.overflow.map(x => x.code)).toEqual(['p4', 'p5'])
    expect(SSO_VISIBLE_MAX).toBe(4)
  })
})

describe('resolveProviderIcon / isIconifyName (I-A)', () => {
  it('known brand codes win over icon field', () => {
    expect(resolveProviderIcon('GitHub', 'mdi:something')).toEqual({
      kind: 'brand',
      code: 'github',
    })
    expect(resolveProviderIcon('wechat')).toEqual({ kind: 'brand', code: 'wechat' })
    expect(resolveProviderIcon('wecom')).toEqual({ kind: 'brand', code: 'wecom' })
  })

  it('accepts Iconify names only', () => {
    expect(isIconifyName('mdi:github')).toBe(true)
    expect(isIconifyName('  ph:link-simple  ')).toBe(true)
    expect(isIconifyName('https://cdn.example/x.svg')).toBe(false)
    expect(isIconifyName('//evil/x')).toBe(false)
    expect(isIconifyName('data:image/svg+xml;base64,xx')).toBe(false)
    expect(isIconifyName('')).toBe(false)
    expect(isIconifyName(null)).toBe(false)
  })

  it('unknown code + iconify → iconify; URL → letter fallback', () => {
    expect(resolveProviderIcon('keycloak', 'mdi:key')).toEqual({ kind: 'iconify', name: 'mdi:key' })
    expect(resolveProviderIcon('keycloak', 'https://x/y.png')).toEqual({
      kind: 'letter',
      letter: 'K',
    })
    expect(resolveProviderIcon('oidc', '')).toEqual({ kind: 'letter', letter: 'O' })
  })
})

describe('claimWeComAutoLogin', () => {
  // 企业微信 FAQ 里的客户端 UA 形态:带 wxwork,也带微信的 MicroMessenger
  const WECOM_UA =
    'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/14F89 wxwork/4.1.20 MicroMessenger/7.0.1'
  const WECHAT_UA = 'Mozilla/5.0 (Linux; Android 14) Mobile Safari/537.36 MicroMessenger/8.0.49'
  const CHROME_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/140.0 Safari/537.36'
  const withWeCom = [{ code: 'github' }, { code: 'wecom' }]

  it('fires once per session inside the WeCom client when wecom is enabled', () => {
    const storage = memoryStorage()
    const opts = { userAgent: WECOM_UA, providers: withWeCom, storage }
    expect(claimWeComAutoLogin(opts)).toBe(true)
    // 登录失败或主动退出后回到登录页:同一会话不再自动跳
    expect(claimWeComAutoLogin(opts)).toBe(false)
  })

  it('stays on the login page outside the WeCom client or when wecom is not enabled', () => {
    for (const userAgent of [WECHAT_UA, CHROME_UA]) {
      expect(
        claimWeComAutoLogin({ userAgent, providers: withWeCom, storage: memoryStorage() }),
      ).toBe(false)
    }
    expect(
      claimWeComAutoLogin({
        userAgent: WECOM_UA,
        providers: [{ code: 'github' }],
        storage: memoryStorage(),
      }),
    ).toBe(false)
  })

  it('does not fire when storage is unavailable (cannot remember it already tried)', () => {
    const broken = {
      getItem: () => {
        throw new Error('SecurityError')
      },
      setItem: () => {
        throw new Error('SecurityError')
      },
    }
    expect(
      claimWeComAutoLogin({ userAgent: WECOM_UA, providers: withWeCom, storage: broken }),
    ).toBe(false)
  })
})

describe('mergeBindingRows (B-A)', () => {
  it('lists all enabled providers without N=4 truncate', () => {
    const providers = Array.from({ length: 6 }, (_, i) => ({
      code: `p${i}`,
      displayName: `P${i}`,
    }))
    const rows = mergeBindingRows(providers, [])
    expect(rows).toHaveLength(6)
    expect(rows.every(r => r.enabled)).toBe(true)
  })

  it('appends disabled-but-bound after enabled list', () => {
    const providers = [{ code: 'github', displayName: 'GitHub' }]
    const bindings = [
      { provider: 'github', displayName: 'octocat', boundAt: '2026-01-01T00:00:00Z' },
      { provider: 'wechat', displayName: null, boundAt: '2026-01-02T00:00:00Z' },
    ]
    const rows = mergeBindingRows(providers, bindings)
    expect(rows.map(r => r.code)).toEqual(['github', 'wechat'])
    expect(rows[0].enabled).toBe(true)
    expect(rows[0].binding?.boundAt).toBe('2026-01-01T00:00:00Z')
    expect(rows[1].enabled).toBe(false)
    expect(rows[1].binding).toBeTruthy()
    expect(rows[1].displayName).toBe('wechat')
  })

  it('enabled with no binding still appears for bind action', () => {
    const rows = mergeBindingRows([{ code: 'dingtalk', displayName: '钉钉' }], [])
    expect(rows).toHaveLength(1)
    expect(rows[0].binding).toBeUndefined()
    expect(rows[0].enabled).toBe(true)
  })
})
