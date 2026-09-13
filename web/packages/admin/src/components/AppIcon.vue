<script setup lang="ts">
// SmartAdmin 图标渲染器:钉死全站的空值兜底图标,并按「是否在离线子集里」分流。
// 子集(setupIcons 启动时同步注册)里的图标直接渲染;子集外的名字——多半是消费者在菜单管理里配的——
// 交给包的 SmartIcon,由它懒加载整集兜底。不分流的话,一个库里配的图标就会把整套 ph 拉回来。
import { computed } from 'vue'
import { Icon, iconLoaded } from '@iconify/vue'
import { SmartIcon } from 'smart-naive-icon'

const props = defineProps<{ icon?: string; size?: number | string }>()

// 兜底与 useLayoutMenu.renderIcon 一致,保证 rail/折叠态也有图标
const FALLBACK = 'ph:dot-outline-duotone'

const name = computed(() => props.icon?.trim() || FALLBACK)
const px = computed(() =>
  typeof props.size === 'number' ? `${props.size}px` : (props.size ?? '18px'),
)
// iconLoaded 非响应式,取一次即可:子集是启动同步装的,此后只增不减。
const bundled = computed(() => iconLoaded(name.value))
</script>

<template>
  <Icon v-if="bundled" :icon="name" :width="px" :height="px" />
  <SmartIcon v-else :icon="name" :size="px" :fallback="FALLBACK" />
</template>
