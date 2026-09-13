<script setup lang="ts">
// 站点徽标,侧栏/顶栏/登录页/应用选择页共用,品牌只在这一处生效。取值顺序:
//   1) 后台「站点 logo」配置(sys.site.logo)有图片地址 → <img>;
//   2) createSmartAdmin({ brand: { logo } }) 传的组件或图片地址(sys.site.logo 为空时使用);
//   3) 内置 SmartAdmin 矢量标(Neural Hex:装甲六边形壳 + 7 节点神经网络,中心点亮)。内联 SVG、固定品牌配色
//      (不随 accent 变——品牌身份固定 #646CFF);明暗两版切换,与模板 public/smart-logo.svg、smart-logo-dark.svg 是同一几何,改图形要三处同步。
import { computed } from 'vue'
import { useAppStore } from '#/stores/app'
import { useSite } from '#/composables/useSite'
import { runtime } from '#/lib/runtime'

const props = withDefaults(defineProps<{ size?: number }>(), { size: 28 })
const app = useAppStore()
const { site } = useSite()

const brandLogo = runtime.brand.logo
const imgSrc = computed(() => site.logo || (typeof brandLogo === 'string' ? brandLogo : ''))
const brandComponent = computed(() =>
  !site.logo && brandLogo && typeof brandLogo !== 'string' ? brandLogo : null,
)

// 浅色:靛蓝渐变底 + 白色图形 + 浅青核心;深色:近黑底(带青色微光)+ 靛蓝→青渐变图形 + 青色核心
const bg = computed(() => (app.isDark ? '#0F1222' : 'url(#sa-logo-tile)'))
const stroke = computed(() => (app.isDark ? 'url(#sa-logo-mark)' : '#FFFFFF'))
const core = computed(() => (app.isDark ? '#22D3EE' : '#A5F3FC'))
</script>

<template>
  <!-- 配置的 logo 可能是横版:定高、宽度随比例,容器窄时被 max-width 收住 -->
  <img
    v-if="imgSrc"
    :src="imgSrc"
    :style="{ height: `${props.size}px` }"
    :alt="site.title"
    class="site-logo"
  />
  <component :is="brandComponent" v-else-if="brandComponent" :size="props.size" />
  <svg
    v-else
    :width="props.size"
    :height="props.size"
    viewBox="0 0 120 120"
    role="img"
    aria-label="SmartAdmin"
  >
    <defs>
      <linearGradient
        id="sa-logo-tile"
        gradientUnits="userSpaceOnUse"
        x1="0"
        y1="0"
        x2="120"
        y2="120"
      >
        <stop offset="0" stop-color="#7C83FF" />
        <stop offset="1" stop-color="#4B52E6" />
      </linearGradient>
      <linearGradient id="sa-logo-mark" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="#646CFF" />
        <stop offset="1" stop-color="#22D3EE" />
      </linearGradient>
      <radialGradient id="sa-logo-glow" cx="0.5" cy="0.45" r="0.55">
        <stop offset="0" stop-color="#22D3EE" stop-opacity="0.26" />
        <stop offset="1" stop-color="#22D3EE" stop-opacity="0" />
      </radialGradient>
    </defs>
    <rect width="120" height="120" rx="27" :fill="bg" />
    <rect v-if="app.isDark" width="120" height="120" rx="27" fill="url(#sa-logo-glow)" />
    <polygon
      points="60,20 94.6,40 94.6,80 60,100 25.4,80 25.4,40"
      fill="none"
      :stroke="stroke"
      stroke-width="11"
      stroke-linejoin="round"
    />
    <path
      d="M60 60L60 39M60 60L78.2 49.5M60 60L78.2 70.5M60 60L60 81M60 60L41.8 70.5M60 60L41.8 49.5"
      fill="none"
      :stroke="stroke"
      stroke-width="3.5"
      stroke-linecap="round"
    />
    <g :fill="stroke">
      <circle cx="60" cy="39" r="5" />
      <circle cx="78.2" cy="49.5" r="5" />
      <circle cx="78.2" cy="70.5" r="5" />
      <circle cx="60" cy="81" r="5" />
      <circle cx="41.8" cy="70.5" r="5" />
      <circle cx="41.8" cy="49.5" r="5" />
    </g>
    <circle cx="60" cy="60" r="8.5" :fill="core" />
  </svg>
</template>

<style scoped>
.site-logo {
  display: block;
  width: auto;
  max-width: 100%;
  object-fit: contain;
}
</style>
