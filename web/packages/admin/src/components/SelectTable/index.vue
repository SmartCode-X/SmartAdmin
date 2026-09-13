<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import {
  NButton,
  NDataTable,
  NInput,
  NModal,
  NSpace,
  useMessage,
  type DataTableColumns,
  type PaginationProps,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '#/components/AppIcon.vue'
import { translateError } from '#/utils/error'

type RowKey = string | number
type SelectTableRow = any
type PageResult = { items: SelectTableRow[]; total: number }

interface SelectTableSearchField {
  key: string
  label: string
  placeholder?: string
}

const value = defineModel<any | null>('value', { default: null })
const selected = defineModel<SelectTableRow | null>('selected', { default: null })
const values = defineModel<RowKey[]>('values', { default: () => [] })
const selecteds = defineModel<SelectTableRow[]>('selecteds', { default: () => [] })
const emit = defineEmits<{ confirm: [] }>()

const props = withDefaults(
  defineProps<{
    title: string
    placeholder?: string
    disabled?: boolean
    columns: DataTableColumns<SelectTableRow>
    searchFields?: SelectTableSearchField[]
    keywordSearch?: boolean
    keywordSearchPlaceholder?: string
    keywordParam?: string
    fetcher: (
      params: { page: number; pageSize: number } & Record<string, unknown>,
    ) => Promise<PageResult>
    rowKey: (row: SelectTableRow) => RowKey
    label: (row: SelectTableRow) => string
    multiple?: boolean
    displaySelected?: boolean
    triggerVisible?: boolean
    width?: number
    pageSize?: number
    /** false = 不分页:一次取 unpagedSize 条,表体内滚动。数据量小的档案(岗位、字典项…)这样翻找更快 */
    paginated?: boolean
    /** 不分页时单次拉取的上限,超过它的数据仍需靠查询框收窄 */
    unpagedSize?: number
  }>(),
  {
    placeholder: '',
    disabled: false,
    searchFields: () => [],
    keywordSearch: false,
    keywordSearchPlaceholder: '',
    keywordParam: 'keyword',
    width: 860,
    pageSize: 10,
    multiple: false,
    displaySelected: true,
    triggerVisible: true,
    paginated: true,
    unpagedSize: 200,
  },
)

const { t } = useI18n()
const message = useMessage()
const show = ref(false)
const loading = ref(false)
const rows = ref<SelectTableRow[]>([])
const selectedKey = ref<RowKey | null>(null)
const query = reactive<Record<string, string>>({})
const keyword = ref('')

const displayText = computed(() =>
  props.multiple
    ? props.displaySelected
      ? selecteds.value.map(row => props.label(row)).join(', ')
      : ''
    : selected.value
      ? props.label(selected.value)
      : '',
)
const currentRow = computed(
  () => rows.value.find(row => props.rowKey(row) === selectedKey.value) ?? null,
)
const hasMultipleSelection = computed(() => values.value.length > 0)

const pagination = reactive<PaginationProps>({
  page: 1,
  pageSize: props.pageSize,
  itemCount: 0,
  showSizePicker: true,
  pageSizes: [10, 20, 50],
  onChange: page => {
    pagination.page = page
    void load()
  },
  onUpdatePageSize: pageSize => {
    pagination.pageSize = pageSize
    pagination.page = 1
    void load()
  },
})

watch(
  () => props.searchFields,
  fields => {
    fields.forEach(field => {
      if (query[field.key] == null) query[field.key] = ''
    })
  },
  { immediate: true },
)

watch(selected, next => {
  if (next) value.value = props.rowKey(next)
})

async function load() {
  loading.value = true
  try {
    const filters = props.keywordSearch
      ? keyword.value.trim()
        ? { [props.keywordParam]: keyword.value.trim() }
        : {}
      : Object.fromEntries(
          props.searchFields
            .map(field => [field.key, query[field.key]?.trim()])
            .filter((entry): entry is [string, string] => Boolean(entry[1])),
        )
    const page = await props.fetcher({
      page: props.paginated ? (pagination.page ?? 1) : 1,
      pageSize: props.paginated ? (pagination.pageSize ?? props.pageSize) : props.unpagedSize,
      ...filters,
    })
    rows.value = page.items
    pagination.itemCount = page.total
    if (!rows.value.some(row => props.rowKey(row) === selectedKey.value))
      selectedKey.value = value.value
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

function open() {
  if (props.disabled) return
  selectedKey.value = value.value
  show.value = true
  void load()
}

defineExpose({ open })

function search() {
  pagination.page = 1
  void load()
}

function reset() {
  if (props.keywordSearch) {
    keyword.value = ''
    search()
    return
  }
  props.searchFields.forEach(field => {
    query[field.key] = ''
  })
  search()
}

function updateCheckedKeys(keys: RowKey[]) {
  values.value = keys
}

function confirm(row = currentRow.value) {
  if (props.multiple) {
    const currentRows = new Map(rows.value.map(item => [props.rowKey(item), item]))
    const preservedRows = selecteds.value.filter(
      item => values.value.includes(props.rowKey(item)) && !currentRows.has(props.rowKey(item)),
    )
    const pickedRows = values.value.map(key => currentRows.get(key)).filter(Boolean)
    selecteds.value = [...preservedRows, ...pickedRows]
    emit('confirm')
    show.value = false
    return
  }
  if (!row) return
  value.value = props.rowKey(row)
  selected.value = row
  emit('confirm')
  show.value = false
}

function clear() {
  if (props.multiple) {
    values.value = []
    selecteds.value = []
  } else {
    value.value = null
    selected.value = null
  }
}
</script>

<template>
  <n-input
    v-if="triggerVisible"
    :value="displayText"
    :placeholder="placeholder || title"
    :disabled="disabled"
    readonly
    clearable
    @click="open"
    @clear="clear"
  >
    <template #suffix>
      <AppIcon icon="ph:magnifying-glass" :size="16" />
    </template>
  </n-input>

  <n-modal
    v-model:show="show"
    preset="card"
    :title="title"
    :style="{ width: `${width}px` }"
    :mask-closable="false"
  >
    <div class="select-table">
      <!-- 用普通 flex 容器而不是 n-space:查询框要吃掉整行剩余宽度(占位文案可能很长),
           n-space 会把每个子项包一层内容宽度的 div,撑不开。 -->
      <div v-if="keywordSearch || searchFields.length" class="select-table-search">
        <n-input
          v-if="keywordSearch"
          v-model:value="keyword"
          class="keyword-input"
          clearable
          :placeholder="keywordSearchPlaceholder || t('common.search')"
          @keyup.enter="search"
        />
        <template v-else>
          <n-input
            v-for="field in searchFields"
            :key="field.key"
            v-model:value="query[field.key]"
            clearable
            :placeholder="field.placeholder || field.label"
            @keyup.enter="search"
          />
        </template>
        <n-button type="primary" @click="search">
          <template #icon><AppIcon icon="ph:magnifying-glass" :size="16" /></template>
          {{ t('common.search') }}
        </n-button>
        <n-button @click="reset">
          <template #icon><AppIcon icon="ph:arrow-counter-clockwise" :size="16" /></template>
          {{ t('common.reset') }}
        </n-button>
      </div>

      <n-data-table
        class="select-table-grid"
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="rowKey"
        :checked-row-keys="props.multiple ? values : undefined"
        @update:checked-row-keys="
          keys => {
            if (props.multiple) updateCheckedKeys(keys as RowKey[])
          }
        "
        :row-props="
          (row: SelectTableRow) => ({
            style: 'cursor: pointer',
            onClick: () => {
              if (!props.multiple) selectedKey = rowKey(row)
            },
            onDblclick: () => {
              if (props.multiple && !values.includes(rowKey(row))) values = [...values, rowKey(row)]
              confirm(row)
            },
          })
        "
        :row-class-name="
          (row: SelectTableRow) => (rowKey(row) === selectedKey ? 'select-table-row-selected' : '')
        "
        :pagination="paginated ? pagination : false"
        :max-height="430"
        size="small"
        remote
      />
    </div>

    <template #footer>
      <n-space justify="end">
        <n-button @click="show = false">{{ t('common.cancel') }}</n-button>
        <n-button
          type="primary"
          :disabled="props.multiple ? !hasMultipleSelection : !currentRow"
          @click="confirm()"
        >
          {{ t('common.confirm') }}
        </n-button>
      </n-space>
    </template>
  </n-modal>
</template>

<style scoped>
.select-table {
  height: 520px;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.select-table-search {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
/* 多字段模式:每个框放得下「编号」这类字段名占位 */
.select-table-search :deep(.n-input) {
  flex: 0 1 200px;
}
/* 单框模糊查询:占位文案是「请输入编号或名称」这种整句,英文更长,
   所以让它吃掉整行剩余宽度,而不是写死一个迟早不够用的像素值。 */
.select-table-search .keyword-input {
  flex: 1 1 320px;
  min-width: 260px;
}
.select-table-search :deep(.n-button) {
  flex: none;
}
.select-table-grid {
  flex: 1;
  min-height: 0;
}
:deep(.select-table-row-selected td) {
  background-color: var(--table-selected-bg);
}
</style>
