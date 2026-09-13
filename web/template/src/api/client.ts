import { createApiClient, type KernelPaths } from 'smart-admin-web'

// 本应用的类型化客户端,中间件链(超时、Bearer + CSRF、401 刷新重放、40024 再认证)与内核客户端同一条。
// 有了自己的端点后,对着自己的后端跑 `npm run gen:api` 生成 src/api/schema.d.ts,
// 再把下面的 KernelPaths 换成 `import type { paths } from './schema'` 的 paths。
// 自己的端点写在 api/<域>.ts,从 smart-admin-web 导入 unwrap / pageParams / toPage / ApiError。
export const client = createApiClient<KernelPaths>()
