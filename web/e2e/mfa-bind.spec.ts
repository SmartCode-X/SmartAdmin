import { expect, test } from '@playwright/test'
import { apiAdminToken, apiReauthWithPassword, apiSetTotpEnabled, seedForceTotpUser } from './api'
import { computeTotp } from './totp'

/**
 * 真实后端:建用户 → 浏览器自助绑定(账号+密码)→ TOTP 完成 → 恢复码展示。
 *
 * TOTP 能力默认关,本文件自己开、用完关。不在 `playwright.config.ts` 里用部署级的
 * `Security:Totp:Enabled` 打开,因为那一把是「硬开地板」:运行时关不掉,而 TOTP 一开,
 * `[RequireReauth]` 就在建用户、角色授权、配置写、菜单写、强退这些端点上全部生效,
 * 别的 spec 会成片拿到 40024。
 */
let seeded: { account: string; password: string }

test.beforeAll(async ({ playwright }) => {
  const request = await playwright.request.newContext()
  try {
    // 顺序要紧:建用户必须发生在打开 TOTP **之前**——这个端点挂着 [RequireReauth],
    // TOTP 关着时它是空操作,开了就要先拿再认证授予。
    seeded = await seedForceTotpUser(request)
    await apiSetTotpEnabled(request, await apiAdminToken(request), true)
  } finally {
    await request.dispose()
  }
})

test.afterAll(async ({ playwright }) => {
  const request = await playwright.request.newContext()
  try {
    const token = await apiAdminToken(request)
    // 关回去之前先拿授予:此刻 TOTP 是开的,配置写端点要再认证。
    // 漏关的话,按文件名排在本文件之后的 spec 会全部撞上 40024,而报出来只是一句超时。
    await apiReauthWithPassword(request, token)
    await apiSetTotpEnabled(request, token, false)
  } finally {
    await request.dispose()
  }
})

test('MFA bind: self-service account+password → authenticator → recovery codes', async ({
  page,
}) => {
  test.setTimeout(60_000)
  const { account, password } = seeded
  await page.goto(`/mfa/bind?account=${encodeURIComponent(account)}`)
  await expect(page.getByText(/设置身份验证器|设置认证器/i).first()).toBeVisible()

  // 账号可能已预填;密码必填
  const accountInput = page.locator('input:not([type="password"]):not([readonly])').first()
  if (!(await accountInput.inputValue()).trim()) {
    await accountInput.fill(account)
  }
  await page.locator('input[type="password"]').fill(password)
  await page.getByRole('button', { name: /开始设置/ }).click()

  const seedInput = page.locator('input[readonly]').first()
  await expect(seedInput).toBeVisible({ timeout: 15_000 })
  const seed = (await seedInput.inputValue()).trim()
  expect(seed.length).toBeGreaterThan(10)

  const code = computeTotp(seed)
  await page.locator('input:not([readonly]):not([type="password"])').last().fill(code)
  await page.getByRole('button', { name: /完成设置/ }).click()

  await expect(page.locator('.recovery-code').first()).toBeVisible({ timeout: 15_000 })
  const codes = await page.locator('.recovery-code').allInnerTexts()
  expect(codes.filter(c => c.trim().length > 0).length).toBeGreaterThanOrEqual(1)
})

test('MFA bind: empty recoveryCodes never shows success screen', async ({ page }) => {
  await page.route('**/api/v1/auth/mfa/bind/start', async route => {
    await route.fulfill({
      json: {
        code: 0,
        data: {
          bindChallengeId: 'chal-e2e',
          otpauthUri: 'otpauth://totp/Smart:e2e?secret=JBSWY3DPEHPK3PXP',
          seed: 'JBSWY3DPEHPK3PXP',
        },
      },
    })
  })
  await page.route('**/api/v1/auth/mfa/bind/complete', async route => {
    await route.fulfill({ json: { code: 0, data: { recoveryCodes: [] } } })
  })

  await page.goto('/mfa/bind')
  await page.locator('input:not([type="password"]):not([readonly])').first().fill('e2euser')
  await page.locator('input[type="password"]').fill('whatever')
  await page.getByRole('button', { name: /开始设置/ }).click()
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible({ timeout: 10_000 })

  await page.locator('input:not([readonly]):not([type="password"])').last().fill('123456')
  await page.getByRole('button', { name: /完成设置/ }).click()

  await expect(page.getByText(/设置响应不完整|未能返回恢复码|重新开始/i)).toBeVisible({
    timeout: 10_000,
  })
  await expect(page.locator('.recovery-code')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible()
})
