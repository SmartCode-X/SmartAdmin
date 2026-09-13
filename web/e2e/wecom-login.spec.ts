import { test, expect } from '@playwright/test'

/**
 * 企业微信客户端里打开登录页:前端直接发起企业微信登录,后端按 UA 给网页授权地址(扫码页在客户端里用不了)。
 * e2e 宿主在 playwright.config.ts 里配了一套假的企业微信应用。
 */

// 企业微信客户端的 UA 带 wxwork,也带微信的 MicroMessenger
const WECOM_UA =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) ' +
  'Mobile/15E148 wxwork/4.1.20 MicroMessenger/7.0.1 Language/zh'

test.use({ userAgent: WECOM_UA })

test('企业微信客户端里打开登录页 → 直接走网页授权;同一会话回到登录页不再自动跳', async ({ page }) => {
  // 真打到后端拿它的 302,但不让浏览器跟去企业微信:page.route 只拦重定向链的第一跳,
  // 拦 302 的目标拦不住,只能在发起授权这一跳上截住
  let authorizeHits = 0
  let location = ''
  await page.route('**/api/v1/auth/external/wecom/authorize', async (route) => {
    authorizeHits++
    const resp = await route.fetch({ maxRedirects: 0 })
    location = resp.headers()['location'] ?? ''
    await route.fulfill({ status: 200, contentType: 'text/html', body: '<p>wecom</p>' })
  })

  await page.goto('/login')
  await expect.poll(() => location, { timeout: 10_000 }).not.toBe('')

  const url = new URL(location)
  expect(`${url.origin}${url.pathname}`).toBe('https://open.weixin.qq.com/connect/oauth2/authorize')
  expect(url.searchParams.get('appid')).toBe('ww-e2e-corp')
  expect(url.searchParams.get('agentid')).toBe('1000002')
  expect(url.searchParams.get('response_type')).toBe('code')
  expect(url.searchParams.get('scope')).toBe('snsapi_base')
  expect(url.searchParams.get('state')).toMatch(/^[A-Za-z0-9]{1,128}$/)
  expect(url.searchParams.get('redirect_uri')).toMatch(/\/api\/v1\/auth\/external\/wecom\/callback$/)
  expect(url.hash).toBe('#wechat_redirect')

  // 登录失败或主动退出后回到登录页:停在这里让人自己选,不来回跳
  await page.goto('/login')
  await expect(page.getByPlaceholder(/账号|account/i)).toBeVisible()
  await page.waitForLoadState('networkidle')
  expect(authorizeHits).toBe(1)
})
