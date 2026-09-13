import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'

// 页面 key 规则:views 用 import.meta.glob('./views/**/*.vue') 采集,key 取 /views/ 之后去掉 .vue
// 的相对路径(如 system/user/index),菜单管理里的「组件路径」按同一规则填;与内核内置页同名即覆盖。
// locales 同理按 ext/<locale>/<模块>.ts 采集,eager 加载后按命名空间与内置文案深合并。
// 需要布局壳之外的顶级路由(整屏看板等)、菜单角标、顶栏工具时,分别传 routes / install。
// 前端与 API 不同源(CDN / 独立域名)时构建期给 VITE_API_BASE=https://api.example.com,经 apiBase 交给内核。
createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
  version: __APP_VERSION__,
}).mount('#app')
