/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import dts from 'vite-plugin-dts'

const pkg = JSON.parse(
  readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf-8'),
) as {
  version: string
  dependencies: Record<string, string>
  peerDependencies: Record<string, string>
}

// 库模式:只打包自己的源码,依赖一律外置 —— 消费方装什么版本用什么版本,peer 单实例(vue/pinia/router/i18n)才守得住。
const externals = [...Object.keys(pkg.dependencies), ...Object.keys(pkg.peerDependencies)]
const isExternal = (id: string) => externals.some(p => id === p || id.startsWith(`${p}/`))

export default defineConfig({
  plugins: [
    vue(),
    // 随构建产出 .d.ts(含 .vue),tsconfig 的 #/ 路径别名会被改写成相对路径;schema.d.ts 原样拷进 dist。
    dts({
      tsconfigPath: './tsconfig.json',
      processor: 'vue',
      exclude: ['src/**/*.spec.ts'],
      copyDtsFiles: true,
    }),
  ],
  // 包内别名 #/ → src/(tsconfig 的 paths 同一份);只在本包构建与单测时生效,产物里已全部打平成相对引用。
  resolve: {
    alias: [{ find: /^#\//, replacement: `${fileURLToPath(new URL('./src', import.meta.url))}/` }],
  },
  // 内核版本号是库的构建期常量(登录页页脚默认展示);消费方的应用版本走 createSmartAdmin({ version })。
  define: { __SMART_ADMIN_VERSION__: JSON.stringify(pkg.version) },
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'], cssFileName: 'style' },
    sourcemap: true,
    rollupOptions: {
      external: isExternal,
      // 按源文件一对一输出,不合成大块:消费方打包时才能按页面重新拆分。合成一块的话,入口会静态带上
      // md-editor / echarts / highlight.js(只有通知页、工作台、代码块用),首屏跟着全吃进去。
      output: { preserveModules: true, preserveModulesRoot: 'src', entryFileNames: '[name].js' },
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.spec.ts'],
    globals: false,
    pool: 'forks',
    restoreMocks: true,
    unstubGlobals: true,
    // 这几个依赖在自己的入口里 import CSS,Node 直接加载会报 Unknown file extension ".css";交给 Vite 转换。
    // import 整个包入口的用例(src/index.spec.ts)需要它。
    server: { deps: { inline: ['smart-naive-table', 'smart-naive-icon', 'md-editor-v3'] } },
  },
})
