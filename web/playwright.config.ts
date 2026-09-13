import { defineConfig } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { tmpdir } from 'node:os'
import { join, sep } from 'node:path'
import { resolvePortPair } from './e2e/portPair.mjs'

/**
 * 收口配置:固定 127.0.0.1 + 本 run 端口;reuseExistingServer 恒 false。
 * 忽略 SMART_WEB_BASE。CI 必须注入唯一 SMART_E2E_*_PORT。
 */
/**
 * 摘掉代理变量。**必须在本进程里摘**,只给 webServer 的 env 加 NO_PROXY 不管用:
 * 等 webServer 就绪的那次探测是 Playwright 自己发的,走的是本进程这份 env。
 *
 * 症状很有迷惑性:开发机全局挂着 HTTP_PROXY 时,探测**在服务启动之前**就返回 503
 * (端口上什么都没有本该是 ECONNREFUSED),启动之后依然 503,于是一路等到超时,
 * 报出来却是「Timed out waiting from config.webServer」,看着像后端起不来。
 * e2e 全程只访问 127.0.0.1,不需要任何代理。
 */
for (const key of [
  'HTTP_PROXY',
  'http_proxy',
  'HTTPS_PROXY',
  'https_proxy',
  'ALL_PROXY',
  'all_proxy',
]) {
  delete process.env[key]
}
process.env.NO_PROXY = '127.0.0.1,localhost'
process.env.no_proxy = '127.0.0.1,localhost'

const { apiPort, webPort } = await resolvePortPair({ apiMin: 21000, webMin: 32000, span: 4000 })
const webUrl = `http://127.0.0.1:${webPort}`
const apiUrl = `http://127.0.0.1:${apiPort}`
process.env.SMART_E2E_API_BASE = apiUrl
const adminPassword = process.env.SMART_E2E_PASSWORD ?? 'Aa123456'
const databaseFile = join(tmpdir(), `smart-admin-e2e-vue-${randomUUID()}.db`)
const backendOutput = `${join(tmpdir(), `smart-admin-e2e-vue-build-${randomUUID()}`)}${sep}`

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: `dotnet run --no-launch-profile --project ../backend/samples/MinimalHost -p:BaseOutputPath=${backendOutput}`,
      url: `${apiUrl}/health`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_URLS: apiUrl,
        ASPNETCORE_ENVIRONMENT: 'Development',
        SmartAdmin__Database__ConnectionString: `Data Source=${databaseFile}`,
        SmartAdmin__Seed__AdminPassword: adminPassword,
        /**
         * 关掉限流。整套 e2e 全部从同一个 127.0.0.1 打过去,两道默认阈值都会撞上:
         * 认证端点每 IP 每 60 秒 20 次(`AuthPermitPerWindow`),全局每 IP 每 60 秒 300 次(`PermitPerWindow`)。
         * 命中后回的是 code=40008,信封里连 msgKey 都没有,报出来像密码错了,排查方向直接被带偏。
         * 限流本身不是这套用例要验的东西。
         */
        SmartAdmin__Security__RateLimit__Enabled: 'false',
        /**
         * 一套假的企业微信应用,给 wecom-login 验「企业微信客户端里直接走网页授权」。
         * 只有 UA 带 wxwork 时登录页才会自动跳,其余用例用的是默认 UA,只是登录页多一颗企业微信按钮。
         */
        SmartAdmin__ExternalAuth__WeCom__CorpId: 'ww-e2e-corp',
        SmartAdmin__ExternalAuth__WeCom__AgentId: '1000002',
        SmartAdmin__ExternalAuth__WeCom__CorpSecret: 'e2e-secret',
        /**
         * 这里**不要**打开 TOTP。部署级的 `Security:Totp:Enabled` 是「硬开地板」——运行时关不掉,
         * 而 TOTP 一开,`[RequireReauth]` 就在建用户、角色授权、配置写、菜单写、强退这些端点上全部生效,
         * 整套管理端用例会成片拿到 40024。真正需要它的只有 mfa-bind 一条,那条自己用运行时开关
         * (`sys.security.totp.enabled`)在本文件生命周期内开合,见 `e2e/mfa-bind.spec.ts`。
         */
      },
    },
    {
      command: `npx vite --host 127.0.0.1 --port ${webPort} --strictPort`,
      cwd: 'template',
      url: webUrl,
      reuseExistingServer: false,
      timeout: 60_000,
      env: { SMART_API_TARGET: apiUrl },
    },
  ],
})
