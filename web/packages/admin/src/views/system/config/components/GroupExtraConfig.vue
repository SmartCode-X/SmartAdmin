<script setup lang="ts">
// 结构化 Tab 的「本组其它配置」:同一分组里没被 Tab 字段认领的行(消费方自定义的键等)列在这里,可改可删。
// 没有这类行时不渲染,内核默认数据下各 Tab 外观不变。
import { computed, h, onMounted, ref, watch } from 'vue'
import {
  NButton,
  NDataTable,
  NDivider,
  NPopconfirm,
  NSpace,
  useMessage,
  type DataTableColumns,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useConfirm } from '#/composables/useConfirm'
import { useAuthStore } from '#/stores/auth'
import { configApi } from '#/api'
import { translateError } from '#/utils/error'
import type { SysConfig } from '#/types/api'
import ConfigFormModal from './ConfigFormModal.vue'
import { bumpConfigRevision, configRevision } from '../groups'

const props = defineProps<{
  /** 本 Tab 管理的分组编码 */
  group: string
  /** 本 Tab 已渲染成字段的键 */
  claimed: readonly string[]
}>()

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const all = ref<SysConfig[]>([])
const loading = ref(false)
const rows = computed(() => {
  const claimed = new Set(props.claimed)
  return all.value.filter(r => !claimed.has(r.configKey))
})

async function load() {
  loading.value = true
  try {
    all.value = await configApi.listByGroup(props.group)
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)
watch(configRevision, load)

const show = ref(false)
const editing = ref<SysConfig | null>(null)
function openEdit(r: SysConfig) {
  editing.value = r
  show.value = true
}

const rowKey = (r: SysConfig) => r.id
const columns: DataTableColumns<SysConfig> = [
  { key: 'configKey', title: () => t('config.key'), ellipsis: { tooltip: true } },
  { key: 'name', title: () => t('config.name') },
  {
    key: 'configValue',
    title: () => t('config.value'),
    ellipsis: { tooltip: true },
    render: r => r.configValue || '—',
  },
  {
    key: 'remark',
    title: () => t('config.remark'),
    ellipsis: { tooltip: true },
    render: r => r.remark || '—',
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    render: r =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('PUT:/api/v1/sys/config/{id}')
          ? h(
              NButton,
              { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) },
              () => t('common.edit'),
            )
          : null,
        authStore.hasPerm('DELETE:/api/v1/sys/config/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => configApi.remove(r.id), t('config.deleted')).then(ok => {
                    if (ok) bumpConfigRevision()
                  }),
              },
              {
                trigger: () =>
                  h(NButton, { size: 'small', quaternary: true, type: 'error' }, () =>
                    t('common.delete'),
                  ),
                default: () => t('config.deleteConfirm', { name: r.name }),
              },
            )
          : null,
      ]),
  },
]
</script>

<template>
  <div class="group-extra">
    <template v-if="rows.length">
      <n-divider title-placement="left">{{ t('config.groupExtra.title') }}</n-divider>
      <p class="group-extra-hint">{{ t('config.groupExtra.hint') }}</p>
      <n-data-table
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="rowKey"
        size="small"
      />
    </template>
    <ConfigFormModal v-model:show="show" :row="editing" />
  </div>
</template>

<style scoped>
.group-extra-hint {
  margin: -6px 0 12px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}
</style>
