<script setup lang="ts">
// 门户重建失败的落点:网络抖动 / 后端 5xx / 限流。会话没问题,所以不清会话,给「重试」与「重新登录」两条路。
// 路由是静态的,不依赖动态路由就绪 —— 恰恰是动态路由建不起来时才会到这里。
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NSpace } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '#/components/AppIcon.vue'
import { useAppStore } from '#/stores/app'
import { useAuthStore } from '#/stores/auth'
import { useUserStore } from '#/stores/user'
import { resetRouter } from '#/router'

const route = useRoute()
const router = useRouter()
const app = useAppStore()
const { t } = useI18n()

const redirect = computed(() => (route.query.redirect as string | undefined) || '/')

// 重试 = 再走一次守卫:routesReady 仍是 false,守卫会重新 enterInitial。
function retry() {
  router.replace(redirect.value)
}

function relogin() {
  resetRouter()
  useAuthStore().reset()
  useUserStore().clear()
  router.replace('/login')
}
</script>

<template>
  <div class="boot-error">
    <AppIcon icon="ph:warning-circle-duotone" :size="72" :style="{ color: app.accent }" />
    <h1>{{ t('bootError.title') }}</h1>
    <p>{{ t('bootError.desc') }}</p>
    <n-space>
      <n-button type="primary" @click="retry">{{ t('bootError.retry') }}</n-button>
      <n-button quaternary @click="relogin">{{ t('bootError.relogin') }}</n-button>
    </n-space>
  </div>
</template>

<style scoped>
.boot-error {
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 24px;
  text-align: center;
  background: var(--color-bg-app);
}
h1 {
  margin: 0;
  color: var(--color-text-primary);
  font-size: var(--font-size-xl);
}
p {
  margin: 0 0 8px;
  max-width: 480px;
  color: var(--color-text-secondary);
}
</style>
