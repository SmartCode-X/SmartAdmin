<script setup lang="ts">
import { h, ref } from 'vue'
import { NButton, NSpace, NTabs, NTabPane, NPopconfirm, useMessage } from 'naive-ui'
import { SmartTable, type SmartTableColumn, type SmartTableInst } from 'smart-naive-table'
import { useI18n } from 'vue-i18n'
import { useConfirm } from '#/composables/useConfirm'
import { useAuthStore } from '#/stores/auth'
import { recycleApi, type RecycleBinItem } from '#/api'
import { translateError } from '#/utils/error'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const types = [
  'user',
  'role',
  'org',
  'position',
  'module',
  'config',
  'dict',
  'menu',
  'job',
] as const
type RecycleType = (typeof types)[number]
const activeTab = ref<RecycleType>('user')
const tableRefs = ref<Record<string, SmartTableInst<RecycleBinItem>>>({})

function setTableRef(type: string) {
  // 函数 ref 的参数类型是宽泛的组件实例,收窄为 SmartTableInst 后存入映射
  return (el: unknown) => {
    if (el) tableRefs.value[type] = el as SmartTableInst<RecycleBinItem>
  }
}

function refreshTab(type: string) {
  tableRefs.value[type]?.refresh()
}

const columns: SmartTableColumn<RecycleBinItem>[] = [
  { type: 'index', title: () => t('common.rowNo'), width: 64, align: 'center' },
  { key: 'name', title: () => t('recycle.name') },
  { key: 'code', title: () => t('recycle.code') },
  { key: 'deletedAt', title: () => t('recycle.deletedAt'), format: 'datetime', width: 180 },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 180,
    fixed: 'right',
    hideInSetting: true,
    render: r =>
      h(NSpace, { size: 4, wrapItem: false }, () => [
        authStore.hasPerm('POST:/api/v1/sys/recycle/{type}/{id}/restore')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => recycleApi.restore(activeTab.value, r.id), t('recycle.restored')).then(
                    ok => {
                      if (ok) refreshTab(activeTab.value)
                    },
                  ),
              },
              {
                trigger: () =>
                  h(NButton, { size: 'small', quaternary: true, type: 'primary' }, () =>
                    t('recycle.restore'),
                  ),
                default: () => t('recycle.restoreConfirm', { name: r.name }),
              },
            )
          : null,
        authStore.hasPerm('DELETE:/api/v1/sys/recycle/{type}/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => recycleApi.purge(activeTab.value, r.id), t('recycle.purged')).then(
                    ok => {
                      if (ok) refreshTab(activeTab.value)
                    },
                  ),
              },
              {
                trigger: () =>
                  h(NButton, { size: 'small', quaternary: true, type: 'error' }, () =>
                    t('recycle.purge'),
                  ),
                default: () => t('recycle.purgeConfirm', { name: r.name }),
              },
            )
          : null,
      ]),
  },
]
</script>

<template>
  <div class="view fill-page">
    <!-- 形状 4:.fill-tabs 把 n-tabs → 面板容器 → .n-tab-pane 一路拉成 flex 列,
         面板里的 SmartTable 就接上高度链了(见 styles/layout.css ⑧)。 -->
    <n-tabs v-model:value="activeTab" type="line" animated class="fill-tabs">
      <n-tab-pane v-for="type in types" :key="type" :name="type" :tab="t(`recycle.tabs.${type}`)">
        <SmartTable
          :default-page-size="100"
          :ref="setTableRef(type)"
          :columns="columns"
          :fetcher="recycleApi.page(type)"
          flex-height
          virtual-scroll
          :storage-key="`sys-recycle-${type}`"
          @error="(e: unknown) => message.error(translateError(e))"
        />
      </n-tab-pane>
    </n-tabs>
  </div>
</template>

<style scoped>
/* display / flex-direction / 整屏高度链都在 styles/layout.css 的 .fill-page,这里只留卡片间距。 */
.view {
  gap: var(--gap-card);
}
</style>
