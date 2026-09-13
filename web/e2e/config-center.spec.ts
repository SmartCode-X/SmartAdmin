import { test, expect, type APIRequestContext, type Page } from '@playwright/test'
import { login, enterApp, SYSTEM_APP } from './helpers'
import { apiAdminToken } from './api'

/**
 * 分类配置中心:sys_config 里的每一行都要能在页面上看到、改到、删到。
 * 结构化 Tab 只渲染自己认领的键,同组其余行落在该 Tab 的「本组其它配置」里;
 * 不属于任何结构化分组的行在「其他配置」里。
 */

async function gotoConfig(page: Page) {
  await login(page)
  // /system/* 只挂在「系统」应用下,不能指望登录后碰巧落在那儿(默认应用是全局可变状态)
  await enterApp(page, SYSTEM_APP)
  await page.goto('/system/config')
  await page.waitForLoadState('networkidle')
}

async function openTab(page: Page, name: RegExp) {
  await page.locator('.n-tabs-tab').filter({ hasText: name }).click()
  await page.waitForLoadState('networkidle')
  // 标签页带切换动画,动画没走完时新旧两个面板同时可见,按面板找元素会一次命中两份
  await expect(activePane(page)).toHaveCount(1)
}

/** FormContainer 可能渲染成 modal 或 drawer,取当前可见的那个。 */
function dialog(page: Page) {
  return page.locator('.n-modal, .n-drawer').filter({ visible: true })
}

function fieldIn(scope: ReturnType<typeof dialog>, label: RegExp) {
  return scope.locator('.n-form-item').filter({ hasText: label }).locator('input, textarea').first()
}

/**
 * 当前显示的 Tab 面板。各 Tab 以 show:lazy 常驻,切走只是隐藏,
 * 不限定面板的话 CSS 选择器会数到别的 Tab 里藏着的行,「看不到」的断言就成了空转。
 */
function activePane(page: Page) {
  return page.locator('.n-tab-pane').filter({ visible: true })
}

const apiBase = () => process.env.SMART_E2E_API_BASE ?? 'http://127.0.0.1:5101'

/** 以超管身份调后端,断言信封 code=0 并返回 data。 */
async function api<T = unknown>(
  request: APIRequestContext,
  method: 'get' | 'post' | 'put' | 'delete',
  path: string,
  data?: unknown,
): Promise<T> {
  const token = await apiAdminToken(request)
  const res = await request[method](`${apiBase()}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
    ...(data === undefined ? {} : { data }),
  })
  const env = (await res.json()) as { code: number; msg?: string; data?: T }
  expect(env.code, `${method.toUpperCase()} ${path} → code=${env.code} ${env.msg ?? ''}`).toBe(0)
  return env.data as T
}

test.describe('分类配置中心', () => {
  test.describe.configure({ mode: 'serial' })

  test('其他配置:新增 → 编辑 → 删除', async ({ page }) => {
    const key = `e2e.other.${Date.now().toString(36)}`
    await gotoConfig(page)
    await openTab(page, /其他配置|Other/)

    // 新增
    await page.getByRole('button', { name: /新增|Add/ }).click()
    const add = dialog(page)
    await expect(add).toBeVisible()
    await fieldIn(add, /配置键|Key/).fill(key)
    await fieldIn(add, /配置名称|Name/).fill('E2E 自定义项')
    await fieldIn(add, /配置值|Value/).fill('v1')
    await fieldIn(add, /分组编码|Group/).fill('e2e')
    await add.getByRole('button', { name: /保存|Save/ }).click()
    await expect(add).toBeHidden({ timeout: 5_000 })

    const row = page.locator('.n-data-table-tr').filter({ hasText: key })
    await expect(row).toBeVisible({ timeout: 5_000 })
    await expect(row).toContainText('v1')

    // 编辑
    await row.getByRole('button', { name: /编辑|Edit/ }).click()
    const edit = dialog(page)
    await expect(edit).toBeVisible()
    await fieldIn(edit, /配置值|Value/).fill('v2')
    await edit.getByRole('button', { name: /保存|Save/ }).click()
    await expect(edit).toBeHidden({ timeout: 5_000 })
    await expect(row).toContainText('v2', { timeout: 5_000 })

    // 删除
    await row.getByRole('button', { name: /删除|Delete/ }).click()
    await page
      .locator('.n-popconfirm')
      .getByRole('button', { name: /确认|确定|Confirm|OK/ })
      .click()
    await expect(row).toHaveCount(0, { timeout: 5_000 })
  })

  test('结构化分组里没被字段认领的行:在所属 Tab 的「本组其它配置」里可改可删,「其他配置」里没有', async ({
    page,
    request,
  }) => {
    const stamp = Date.now().toString(36)
    const jobKey = `e2e.job.watermark.${stamp}`
    const customKey = `e2e.custom.${stamp}`
    await api(request, 'post', '/api/v1/sys/config', {
      configKey: jobKey,
      configValue: '2026-01-01',
      name: 'E2E 同步水位',
      groupCode: 'job',
      sort: 99,
    })
    // 对照项:不归任何结构化 Tab 的分组,应当出现在「其他配置」里——证明那张表确实已经载入
    const customId = await api<number>(request, 'post', '/api/v1/sys/config', {
      configKey: customKey,
      configValue: 'x',
      name: 'E2E 对照项',
      groupCode: 'e2e',
      sort: 99,
    })

    try {
      await gotoConfig(page)
      await openTab(page, /定时任务|Scheduled jobs/)
      const jobPane = activePane(page)
      const extraTitle = jobPane
        .locator('.n-divider')
        .filter({ hasText: /本组其它配置|Other configs in this group/ })
      await expect(extraTitle).toBeVisible()
      const row = jobPane.locator('.n-data-table-tr').filter({ hasText: jobKey })
      await expect(row).toContainText('2026-01-01')

      // 编辑:弹窗回填的就是这一行
      await row.getByRole('button', { name: /编辑|Edit/ }).click()
      const edit = dialog(page)
      await expect(edit).toBeVisible()
      await expect(fieldIn(edit, /配置键|Key/)).toHaveValue(jobKey)
      await fieldIn(edit, /配置值|Value/).fill('2026-02-01')
      await edit.getByRole('button', { name: /保存|Save/ }).click()
      await expect(edit).toBeHidden({ timeout: 5_000 })
      await expect(row).toContainText('2026-02-01', { timeout: 5_000 })

      // 「其他配置」只列不归结构化 Tab 的分组:对照项在,job 组的行不在
      await openTab(page, /其他配置|Other/)
      const otherPane = activePane(page)
      await expect(
        otherPane.locator('.n-data-table-tr').filter({ hasText: customKey }),
      ).toBeVisible()
      await expect(otherPane.locator('.n-data-table-tr').filter({ hasText: jobKey })).toHaveCount(0)

      // 删除:最后一条没被认领的行删掉后,整块不再渲染
      await openTab(page, /定时任务|Scheduled jobs/)
      await row.getByRole('button', { name: /删除|Delete/ }).click()
      await page
        .locator('.n-popconfirm')
        .getByRole('button', { name: /确认|确定|Confirm|OK/ })
        .click()
      await expect(row).toHaveCount(0, { timeout: 5_000 })
      await expect(extraTitle).toHaveCount(0)
    } finally {
      await api(request, 'delete', `/api/v1/sys/config/${customId}`)
    }
  })

  test('安全策略:密码历史防重用条数在页面上可改', async ({ page, request }) => {
    const KEY = 'sys.security.password.historyCount'
    await gotoConfig(page)
    await openTab(page, /安全策略|Security/)
    const pane = activePane(page)
    const field = pane
      .locator('.n-form-item')
      .filter({ hasText: /密码历史防重用条数|Password history/ })
      .locator('input')
    await expect(field).toHaveValue('0')

    try {
      await field.fill('3')
      await field.blur()
      await pane.getByRole('button', { name: /保存|Save/ }).click()
      await expect(page.locator('.n-message').first()).toContainText(/保存成功|Saved/, {
        timeout: 5_000,
      })
      expect(await api<string | null>(request, 'get', `/api/v1/sys/config/value/${KEY}`)).toBe('3')
    } finally {
      await api(request, 'put', '/api/v1/sys/config/batch', [{ configKey: KEY, configValue: '0' }])
    }
  })

  test('第三方登录:企业微信卡片的「按账号自动关联」打开要确认,保存后生效', async ({ page, request }) => {
    const KEY = 'sys.externalauth.wecom.linkByAccount'
    const LINK_LABEL = /按账号自动关联|Link by account/
    await gotoConfig(page)
    await openTab(page, /第三方登录|External login/)
    const pane = activePane(page)

    // 只有企业微信卡片有这个开关(e2e 宿主配了一套假的企业微信应用,卡片是已注册状态)
    await expect(pane.locator('.ea-switch').filter({ hasText: LINK_LABEL })).toHaveCount(1)
    const wecomCard = pane.locator('.ea-card').filter({ has: page.locator('.ea-code', { hasText: /^wecom$/ }) })
    const toggle = wecomCard.locator('.ea-switch').filter({ hasText: LINK_LABEL }).locator('.n-switch')
    await expect(toggle).not.toHaveClass(/n-switch--active/)

    try {
      // 取消确认:开关不动
      await toggle.click()
      const confirm = page.locator('.n-dialog')
      await expect(confirm).toContainText(/同一批人|same people/)
      await confirm.getByRole('button', { name: /取消|Cancel/ }).click()
      await expect(toggle).not.toHaveClass(/n-switch--active/)

      // 确认后打开,点保存才写库
      await toggle.click()
      await page.locator('.n-dialog').getByRole('button', { name: /确认|确定|Confirm/ }).click()
      await expect(toggle).toHaveClass(/n-switch--active/)
      await pane.getByRole('button', { name: /保存|Save/ }).click()
      await expect(page.locator('.n-message').first()).toContainText(/保存成功|Saved/, {
        timeout: 5_000,
      })
      expect(await api<string | null>(request, 'get', `/api/v1/sys/config/value/${KEY}`)).toBe('true')
    } finally {
      await api(request, 'put', '/api/v1/sys/config/batch', [{ configKey: KEY, configValue: 'false' }])
    }
  })

  test('其他配置:分组填成结构化分组时提示它归哪个 Tab', async ({ page }) => {
    await gotoConfig(page)
    await openTab(page, /其他配置|Other/)
    await page.getByRole('button', { name: /新增|Add/ }).click()
    const add = dialog(page)
    await expect(add).toBeVisible()
    const groupItem = add.locator('.n-form-item').filter({ hasText: /分组编码|Group/ })

    await fieldIn(add, /分组编码|Group/).fill('job')
    await expect(groupItem).toContainText(/定时任务|Scheduled jobs/)
    await fieldIn(add, /分组编码|Group/).fill('e2e')
    await expect(groupItem).not.toContainText(/定时任务|Scheduled jobs/)

    await add.getByRole('button', { name: /取消|Cancel/ }).click()
    await expect(add).toBeHidden()
  })
})
