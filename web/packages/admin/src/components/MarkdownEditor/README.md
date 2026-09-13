# MarkdownEditor / MarkdownView

通知公告的 Markdown 编辑与渲染,封 [`md-editor-v3`](https://github.com/imzbf/md-editor-v3)。跟随应用明暗主题(`useAppStore().isDark`)。
**存 Markdown 纯文本**(不存 HTML)。Markdown 本身放行内联 HTML,所以两个组件在 setup 首行调 `setupMarkdown()`,给渲染器全局挂 XSS 过滤插件(`src/lib/markdown.ts`),正文里的 `<img onerror>` 之类不会执行。

## MarkdownEditor(编辑)

| 名称            | 类型             | 说明                          |
| --------------- | ---------------- | ----------------------------- |
| `value?`        | `string \| null` | v-model:value,Markdown 文本。 |
| `@update:value` | `(v: string)`    | 变更。                        |

图片上传走 `fileApi.upload`,插进正文的是后端签发的 **`viewUrl`**(签名直链)。

> **不要用 `storagePath` 当 URL。** 它是存储层的相对路径,而后端**默认不静态托管上传目录**——真去 `UseStaticFiles()` 托管它就是鉴权绕过(整个上传目录任人匿名下载,见文档站[路线 A:单体部署](https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/route-a))。`viewUrl` 指向 `GET /api/v1/sys/file/{id}/view?sig=…`:匿名可取(`<img>` 带不了 Authorization 头),但签名是文件 Id 的 HMAC,伪造不了。
>
> 它是**永久**能力链接(拿得到链接就拿得到图):正文是持久内容,带过期时间的 URL 等于"发布半小时后图片全坏"。撤销手段是删文件或轮换 JWT 密钥。

## MarkdownView(只读渲染)

用 `MdPreview`(较编辑器轻)。`value?: string | null` 传 Markdown 文本。

正文里的链接按 href 形状分流(`src/lib/markdownLinks.ts`,容器上一次 click 委托):

| href 形状                       | 行为                                                             |
| ------------------------------- | ---------------------------------------------------------------- |
| 单个 `/` 开头(`/system/notice`) | `router.push`,不整页刷新;Ctrl/Cmd/Shift + 点击交给浏览器新开标签 |
| `http(s)://…`                   | 新标签打开(`noopener`)                                           |
| 其余(`#锚点`、`mailto:`、`//…`) | 不干预,浏览器原生行为                                            |

`//evil.com` 与 `/\evil.com` 是协议相对 URL,不算站内路径。

## 用法

```vue
<script setup lang="ts">
import { MarkdownEditor, MarkdownView } from 'smart-admin-web'
</script>

<template>
  <!-- 编辑(发布表单) -->
  <MarkdownEditor v-model:value="form.content" />

  <!-- 展示(详情) -->
  <MarkdownView :value="row.content" />
</template>
```
