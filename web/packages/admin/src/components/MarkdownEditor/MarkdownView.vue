<script setup lang="ts">
// 只读渲染 Markdown(MdPreview,较 MdEditor 轻)。跟随应用明暗主题。通知详情/列表展示用。
// XSS 过滤由 setup 首行的 setupMarkdown() 兜底(#/lib/markdown)—— MdPreview 默认会渲染正文里的内联 HTML,
// 必须过滤。no-* 关掉会从 unpkg 懒加载的扩展,气隙下零触网。
// 正文里的链接在容器上做一次 click 委托(#/lib/markdownLinks):站内相对路径走 vue-router 不整页刷新,
// http(s) 外链新标签打开;Ctrl/Cmd + 点击仍交给浏览器。
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { MdPreview } from 'md-editor-v3'
import 'md-editor-v3/lib/style.css'
import { useAppStore } from '#/stores/app'
import { setupMarkdown } from '#/lib/markdown'
import { handleMarkdownLinkClick } from '#/lib/markdownLinks'

// 与编辑器同理:全局配置在组件里挂,别把 md-editor-v3 拖进首屏 chunk。setupMarkdown 幂等。
setupMarkdown()

const props = defineProps<{ value?: string | null }>()
const app = useAppStore()
const router = useRouter()
const theme = computed(() => (app.isDark ? 'dark' : 'light'))

function onClick(ev: MouseEvent) {
  handleMarkdownLinkClick(ev, to => router.push(to))
}
</script>

<template>
  <div @click="onClick">
    <MdPreview :model-value="props.value ?? ''" :theme="theme" no-katex no-mermaid no-highlight />
  </div>
</template>
