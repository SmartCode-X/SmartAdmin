<script setup lang="ts">
// 渲染错误边界:子树里未捕获的渲染/生命周期异常在这里收口。
// 没有它时,Vue 会把整棵子树卸干净 —— 内容区直接变白,侧栏还在,看着像"点了没反应"。
import { nextTick, ref, onErrorCaptured } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NSpace } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '#/components/AppIcon.vue'
import { reloadOnChunkError } from '#/lib/chunkReload'

const { t } = useI18n()
const router = useRouter()

const failed = ref(false)
const detail = ref('')
// 重试靠 v-if 关开一次重挂子树:清掉 failed 而不重挂,出错的组件会立刻再抛一次。
const alive = ref(true)

onErrorCaptured(err => {
  // 发版后旧 chunk 失效:自动重载一次拿新 index.html,这里就不必打扰用户。
  if (reloadOnChunkError(err)) return false
  console.error('[SmartAdmin] 页面渲染异常', err)
  detail.value = err instanceof Error ? err.message : String(err)
  failed.value = true
  return false // 不再向上冒泡:上面没有第二道边界,冒上去就是白屏
})

async function retry() {
  failed.value = false
  alive.value = false
  await nextTick()
  alive.value = true
}

function goHome() {
  failed.value = false
  alive.value = true
  router.replace('/')
}
</script>

<template>
  <div v-if="failed" class="error-boundary">
    <AppIcon icon="ph:warning-circle-duotone" :size="56" />
    <h2>{{ t('errorBoundary.title') }}</h2>
    <p class="desc">{{ t('errorBoundary.desc') }}</p>
    <code v-if="detail" class="detail">{{ detail }}</code>
    <n-space>
      <n-button type="primary" @click="retry">{{ t('errorBoundary.retry') }}</n-button>
      <n-button quaternary @click="goHome">{{ t('errorBoundary.home') }}</n-button>
    </n-space>
  </div>
  <slot v-else-if="alive" />
</template>

<style scoped>
.error-boundary {
  min-height: 60vh;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 24px;
  text-align: center;
  color: var(--color-text-secondary);
}
h2 {
  margin: 0;
  color: var(--color-text-primary);
  font-size: var(--font-size-lg);
}
.desc {
  margin: 0;
  max-width: 460px;
}
.detail {
  max-width: 100%;
  padding: 6px 10px;
  overflow-x: auto;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-bg-container);
  color: var(--color-danger);
  font-family: var(--font-family-mono);
  font-size: var(--font-size-xs);
}
</style>
