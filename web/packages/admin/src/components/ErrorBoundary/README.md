# ErrorBoundary

子树渲染错误的兜底。包住 `layouts/default.vue` 里的 `router-view`,页面组件在渲染 / 生命周期里抛出的未捕获异常
被 `onErrorCaptured` 收下,内容区显示错误卡片(重试 / 回首页)而不是白屏。

```vue
<ErrorBoundary>
  <router-view v-slot="{ Component }">…</router-view>
</ErrorBoundary>
```

## 约定

- **健康态零 DOM**:正常渲染时组件只输出 `<slot />`,不加包裹元素,`styles/layout.css` 的高度链不受影响。
- **重试 = 重挂**:内部 `v-if` 关开一次子树。只清错误标记而不重挂,出错的组件会立刻再抛一次。
- **不向上冒泡**:`onErrorCaptured` 返回 `false`。上面没有第二道边界,冒上去就是整页白屏。
- **chunk 失效不弹卡片**:命中 `lib/chunkReload` 的判定就自动重载一次拿新 `index.html`(每标签页仅一次)。
  发版后旧 hash 文件 404 是最常见的一类,用户不需要看见它。
- **只管渲染期异常**:事件回调、`setTimeout`、未处理的 Promise 拒绝走 `createSmartAdmin` 里挂的全局兜底;
  路由懒加载失败走 `router.onError`。三处共用 `lib/chunkReload`。

文案键 `errorBoundary.*`(zh-CN / en-US)。
