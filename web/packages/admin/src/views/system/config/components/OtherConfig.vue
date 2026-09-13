<script setup lang="ts">
// 其他配置 = 分类配置中心的兜底:只列不归任何结构化 Tab 的分组(含空分组)。
// 结构化分组里没被字段认领的行在各自 Tab 的「本组其它配置」里,这里不重复列。
// 列驱动搜索/分页/竞态交 SmartTable,新增/编辑弹窗是共用的 ConfigFormModal。
import { h, ref, watch } from 'vue'
import { NButton, NSpace, NPopconfirm, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { SmartTable, type SmartTableColumn, type SmartTableInst } from 'smart-naive-table'
import AppIcon from '#/components/AppIcon.vue'
import { useConfirm } from '#/composables/useConfirm'
import { useAuthStore } from '#/stores/auth'
import { configApi } from '#/api'
import { translateError } from '#/utils/error'
import type { SysConfig } from '#/types/api'
import ConfigFormModal from './ConfigFormModal.vue'
import { STRUCTURED_GROUPS, bumpConfigRevision, configRevision } from '../groups'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()
const tableRef = ref<SmartTableInst<SysConfig>>()

const fetchOtherConfigs = (params: Parameters<typeof configApi.page>[0]) =>
  configApi.page({ ...params, excludedGroupCodes: STRUCTURED_GROUPS })

// 任何 Tab 里增删改了配置行都刷当前页(不回第 1 页):改了分组的行要跟着进出本表
watch(configRevision, () => tableRef.value?.refresh())

const columns: SmartTableColumn<SysConfig>[] = [
  { type: 'index', title: () => t('common.rowNo'), width: 64, align: 'center' },
  { key: 'configKey', title: () => t('config.key'), search: true },
  { key: 'name', title: () => t('config.name'), search: true },
  {
    key: 'configValue',
    title: () => t('config.value'),
    ellipsis: { tooltip: true },
    render: r => r.configValue || '—',
  },
  {
    key: 'groupCode',
    title: () => t('config.group'),
    search: true,
    render: r => r.groupCode || '—',
  },
  { key: 'sort', title: () => t('config.sort'), width: 80 },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 140,
    fixed: 'right',
    hideInSetting: true,
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
                // popconfirm 当触发器,「执行→toast」交给 useConfirm().run(与 module 页一致)。
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

const show = ref(false)
const editing = ref<SysConfig | null>(null)
function openAdd() {
  editing.value = null
  show.value = true
}
function openEdit(r: SysConfig) {
  editing.value = r
  show.value = true
}
</script>

<template>
  <SmartTable
    :default-page-size="100"
    ref="tableRef"
    :columns="columns"
    :fetcher="fetchOtherConfigs"
    storage-key="sys-config"
    @error="e => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'POST:/api/v1/sys/config'" type="primary" @click="openAdd">
        <template #icon><AppIcon icon="ph:plus" :size="16" /></template>
        {{ t('common.add') }}
      </n-button>
    </template>
  </SmartTable>

  <ConfigFormModal v-model:show="show" :row="editing" />
</template>
