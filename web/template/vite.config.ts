import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 版本号 = package.json 的 version,构建期注入前端(登录页页脚展示)。打包即固化,不走后端配置。
const appVersion = JSON.parse(
  readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf-8'),
).version

// CORS 默认 deny-all,浏览器只与 :5173 通信,/api、/openapi、/hub 由 dev proxy 反代到后端。
// 后端 dev 端口默认 5100(避开 macOS AirPlay 占的 5000);可用 SMART_API_TARGET 覆盖。
const apiTarget = process.env.SMART_API_TARGET ?? 'http://localhost:5100'

// monorepo 内开发内核页面用的源码别名:仓库里 packages/admin/src/index.ts 存在时,
// dev/preview 直接指向内核源码而不是编译产物,改内核页面能带 HMR;
// degit 出去的独立副本没有 packages/ 目录,这段判断自然落空,回退到装好的 smart-admin-web 包。
// 内核源码里的 #/ 是包内别名(→ packages/admin/src/),走源码时也要一并映射;__SMART_ADMIN_VERSION__ 同理由这里注入。
const adminSrc = fileURLToPath(new URL('../packages/admin/src', import.meta.url))
const adminSrcEntry = `${adminSrc}/index.ts`
const useAdminSource = existsSync(adminSrcEntry)
const adminVersion = useAdminSource
  ? JSON.parse(
      readFileSync(
        fileURLToPath(new URL('../packages/admin/package.json', import.meta.url)),
        'utf-8',
      ),
    ).version
  : ''

export default defineConfig(({ command }) => {
  const adminFromSource = useAdminSource && command === 'serve'
  return {
    plugins: [vue()],
    define: {
      __APP_VERSION__: JSON.stringify(appVersion),
      ...(adminFromSource ? { __SMART_ADMIN_VERSION__: JSON.stringify(adminVersion) } : {}),
    },
    resolve: {
      alias: [
        { find: /^@\//, replacement: `${fileURLToPath(new URL('./src', import.meta.url))}/` },
        ...(adminFromSource
          ? [
              // style.css 在包里是四份样式的合集;源码模式下由 src/index.ts 自己 import 那四份,这里给个空壳即可
              {
                find: /^smart-admin-web\/style\.css$/,
                replacement: `${adminSrc}/styles/empty.css`,
              },
              { find: /^smart-admin-web$/, replacement: adminSrcEntry },
              { find: /^#\//, replacement: `${adminSrc}/` },
            ]
          : []),
      ],
      // 强制单份 peer,防双实例(invalid hook / useThemeVars 失效)。
      dedupe: ['vue', 'vue-router', 'pinia', 'vue-i18n', 'naive-ui', '@iconify/vue'],
    },
    server: {
      port: 5173,
      // 端口被占时必须失败,禁止静默挪到下一个空闲端口去连上别的 Vite 应用。
      strictPort: true,
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/openapi': { target: apiTarget, changeOrigin: true },
        '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
      },
    },
    build: {
      rollupOptions: {
        output: {
          // 手动分包:框架层单独成块(自家代码改版不失效浏览器缓存),重量级三方各自命名成块。
          //
          // 只圈这四组是量出来的,不是省事:
          // - **naive-ui 不能整组**。它按组件天然拆成几十个懒加载小块,强行归成一块会让 data-table /
          //   date-picker 这些只有深层页面用的组件全部变成首屏静态依赖 —— 实测首屏 gz 从 359 KB 涨到 442 KB。
          // - **md-editor 不能整组**。归组后 rollup 把共享模块吸进这块,入口反而静态依赖它(+300 KB gz);
          //   顺带把 `@codemirror/legacy-modes` 上百个本来各自懒加载的语法模式并成一块 1.9 MB 的大饼。
          //   md-editor 只活在通知页的懒加载块里(setupMarkdown 在组件 setup 里调用,不在启动入口)。
          manualChunks(id) {
            if (!id.includes('node_modules')) return
            const pkg = (re: RegExp) => re.test(id.replace(/\\/g, '/'))
            if (pkg(/\/node_modules\/(vue|vue-router|vue-i18n|pinia|@vue|@intlify)\//)) return 'vue'
            if (pkg(/\/node_modules\/(echarts|zrender|vue-echarts)\//)) return 'echarts'
            if (pkg(/\/node_modules\/highlight\.js\//)) return 'hljs'
            if (pkg(/\/node_modules\/@microsoft\/signalr\//)) return 'signalr'
          },
        },
      },
    },
    // vite preview(产物预览)不继承 server.proxy,这里配同一套;低内存机上用 build+preview 代替 dev 跑整站。
    preview: {
      port: 5173,
      // 与 server 同理:预览被占端口时必须失败,禁止静默挪端口连错应用。
      strictPort: true,
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/openapi': { target: apiTarget, changeOrigin: true },
        '/hub': { target: apiTarget, changeOrigin: true, ws: true },
      },
    },
  }
})
