/// <reference types="vite/client" />

// 库构建期注入的内核版本号(vite.config define ← package.json version);消费方可用 createSmartAdmin({ version }) 覆盖
declare const __SMART_ADMIN_VERSION__: string

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}
