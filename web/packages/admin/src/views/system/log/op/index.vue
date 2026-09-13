<script setup lang="ts">
// 操作日志 = 只读 SmartTable + 详情抽屉。后端无 op/{id},分页项已含全字段 → 抽屉直接用行数据。
// 搜索区要能答审计的三个问题:谁(操作人)、什么时候(时间范围)、干了什么(操作名/路径/成败)。
// paramJson 走 CodeBlock(json 高亮 + 复制;美化是 parse→stringify,失败原样);异常堆栈保持危险色 <pre>——堆栈非代码,高亮无意义。
// 导出:ExportColumnsModal 选列 + 当前筛选条件。
import { h, ref } from 'vue'
import {
  NButton,
  NTag,
  NDrawer,
  NDrawerContent,
  NDescriptions,
  NDescriptionsItem,
  useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { SmartTable, type SmartTableColumn, type SmartTableInst } from 'smart-naive-table'
import AppIcon from '#/components/AppIcon.vue'
import CodeBlock from '#/components/CodeBlock/index.vue'
import UserSelect from '#/components/UserSelect/index.vue'
import ExportColumnsModal from '#/components/ExportColumnsModal/index.vue'
import { useConfirm } from '#/composables/useConfirm'
import { logApi } from '#/api'
import { translateError } from '#/utils/error'
import { operatorText } from '#/utils/format'
import { triggerBlobDownload } from '#/utils/download'
import type { ExportColumnDef, SysOpLog } from '#/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const tableRef = ref<SmartTableInst<SysOpLog>>()

const exportShow = ref(false)
const exporting = ref(false)

/** 与后端 OpLogExportProfile.Columns 对齐。 */
const opExportColumns: ExportColumnDef[] = [
  { key: 'Title', title: t('log.opName') },
  { key: 'HttpMethod', title: t('log.method') },
  { key: 'Path', title: t('log.path') },
  { key: 'ResultCode', title: t('log.resultCode') },
  { key: 'Success', title: t('log.success') },
  { key: 'OperatorName', title: t('log.operator') },
  { key: 'Ip', title: t('log.ip') },
  { key: 'ElapsedMs', title: t('log.elapsed') },
  { key: 'CreateTime', title: t('common.createTime') },
  { key: 'ExceptionMessage', title: t('log.exceptionMessage'), defaultSelected: false },
]

async function onExport(keys: string[]) {
  const p = tableRef.value?.params ?? {}
  exporting.value = true
  try {
    const blob = await logApi.opExport({
      title: p.title || undefined,
      success: p.success,
      operatorId: p.operatorId != null ? Number(p.operatorId) : undefined,
      path: p.path || undefined,
      createTime: p.createTime ?? null,
      columns: keys.join(','),
    })
    triggerBlobDownload(blob, t('logOp.fileName'))
    exportShow.value = false
    message.success(t('export.done'))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    exporting.value = false
  }
}

const columns: SmartTableColumn<SysOpLog>[] = [
  { type: 'index', title: () => t('common.rowNo'), width: 64, align: 'center' },
  { key: 'title', title: () => t('log.opName'), search: true },
  { key: 'httpMethod', title: () => t('log.method'), width: 90 },
  { key: 'path', title: () => t('log.path'), ellipsis: { tooltip: true }, search: true },
  {
    key: 'success',
    title: () => t('log.result'),
    tag: true,
    search: true,
    options: [
      { label: () => t('log.success'), value: true, tagType: 'success' },
      { label: () => t('log.failed'), value: false, tagType: 'error' },
    ],
  },
  { key: 'resultCode', title: () => t('log.resultCode'), width: 100 },
  {
    key: 'operator',
    title: () => t('log.operator'),
    width: 120,
    render: r => operatorText(r),
    // 日志只存 OperatorId(姓名是读取时回填的),故按人筛必须是精确 Id → 复用现成的人员选择器,搜索键用 operatorId。
    search: {
      key: 'operatorId',
      label: () => t('log.operator'),
      render: ({ value, setValue, search }) =>
        h(UserSelect, {
          value: value as number | null,
          clearable: true,
          placeholder: t('log.operatorPlaceholder'),
          'onUpdate:value': (v: number | null) => {
            setValue(v)
            search()
          },
        }),
    },
  },
  { key: 'elapsedMs', title: () => t('log.elapsed'), width: 100, render: r => `${r.elapsedMs} ms` },
  { key: 'ip', title: () => t('log.ip'), render: r => r.ip || '—' },
  // 时间范围是审计最常用的一刀("上周三下午谁删了那批数据");daterange 回传 ['YYYY-MM-DD','YYYY-MM-DD'],
  // api 层拆成 StartTime/EndTime 并把结束日补到 23:59:59。
  {
    key: 'createTime',
    title: () => t('common.createTime'),
    format: 'datetime',
    search: { type: 'daterange' },
  },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 90,
    fixed: 'right',
    hideInSetting: true,
    render: r =>
      h(
        NButton,
        { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) },
        () => t('log.detail'),
      ),
  },
]

// ── 详情抽屉(只读,行数据直填)──
const showDetail = ref(false)
const detailRow = ref<SysOpLog | null>(null)
function openDetail(r: SysOpLog) {
  detailRow.value = r
  showDetail.value = true
}
/** paramJson 美化:parse→stringify(2);非法 JSON 原样返回。空值占位由模板判空。 */
function prettyParam(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

function clearLogs() {
  confirm({
    type: 'error',
    content: t('log.clearOpConfirm'),
    action: () => logApi.opClear(),
    successMsg: t('log.cleared'),
  }).then(ok => {
    if (ok) tableRef.value?.refresh()
  })
}
</script>

<template>
  <SmartTable
    :default-page-size="100"
    ref="tableRef"
    :columns="columns"
    :fetcher="logApi.opPage"
    flex-height
    virtual-scroll
    storage-key="sys-log-op"
    @error="e => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'DELETE:/api/v1/sys/log/op'" type="error" secondary @click="clearLogs">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>
        {{ t('log.clear') }}
      </n-button>
      <n-button v-auth="'GET:/api/v1/sys/log/op/export'" @click="exportShow = true">
        <template #icon><AppIcon icon="ph:download-simple" :size="16" /></template>
        {{ t('export.button') }}
      </n-button>
    </template>
  </SmartTable>

  <ExportColumnsModal
    v-model:show="exportShow"
    :columns="opExportColumns"
    :loading="exporting"
    @confirm="onExport"
  />

  <n-drawer v-model:show="showDetail" :width="560" placement="right">
    <n-drawer-content :title="t('log.detail')" closable>
      <n-descriptions v-if="detailRow" label-placement="left" :column="1" bordered size="small">
        <n-descriptions-item :label="t('log.opName')">{{ detailRow.title }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.method')">
          {{ detailRow.httpMethod }}
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.path')">{{ detailRow.path }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.result')">
          <n-tag :type="detailRow.success ? 'success' : 'error'" size="small" :bordered="false">
            {{ detailRow.success ? t('log.success') : t('log.failed') }}
          </n-tag>
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.resultCode')">
          {{ detailRow.resultCode }}
        </n-descriptions-item>
        <n-descriptions-item v-if="detailRow.exceptionMessage" :label="t('log.exception')">
          <pre class="exception">{{ detailRow.exceptionMessage }}</pre>
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.elapsed')">
          {{ detailRow.elapsedMs }} ms
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.operator')">
          {{ operatorText(detailRow) }}
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.ip')">{{ detailRow.ip || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.userAgent')">
          {{ detailRow.userAgent || '—' }}
        </n-descriptions-item>
        <n-descriptions-item :label="t('common.createTime')">
          {{ detailRow.createTime }}
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.param')">
          <CodeBlock v-if="detailRow.paramJson" :code="prettyParam(detailRow.paramJson)" />
          <span v-else>—</span>
        </n-descriptions-item>
      </n-descriptions>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.exception {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 12px;
  line-height: 1.5;
  color: var(--color-danger);
}
</style>
