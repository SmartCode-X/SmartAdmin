# 变体一：树表（Org 模式）

适用于有父子层级的数据（机构、分类、菜单）。

## 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 树表 |
|---|---|---|
| 数据获取 | `fetcher` prop（SmartTable 内部管分页） | 手动 `load()` + `buildTree()` 传 `:data` |
| 分页 | 有 | `:pagination="false"` |
| 搜索 | 列级 `search: true` | 手动 `filterTree()` + `#toolbar` 搜索框 |
| 展开 | 不适用 | 受控 `expanded-row-keys` + 全展/全收按钮 |
| 新增 | 一种 `openAdd()` | `openAdd(parentId)` — 可新增子节点 |
| 刷新 | `tableRef.value?.refresh()` | 调 `load()` 重新拉取 |

## 关键代码模式

```typescript
import { buildTree, expandableIds, filterTree, type Tree } from '#/utils/tree' // 业务模块:from 'smart-admin-web'

const tree = ref<Tree<SysOrg>[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    tree.value = buildTree(await orgApi.list())  // list 接口返回平铺数组
  } finally {
    loading.value = false
  }
}
onMounted(load)

// 搜索：客户端过滤
const keyword = ref('')
const filteredTree = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tree.value
  return filterTree(tree.value, (n) => n.name.toLowerCase().includes(kw))
})

// 展开控制：数据变化时重算展开节点
const expandedKeys = ref<number[]>([])
watch(filteredTree, (t) => (expandedKeys.value = expandableIds(t)), { immediate: true })
```

## Template 骨架

```vue
<!-- 顶层不是裸 SmartTable(外面还有统计条 / 说明卡)时,外壳加 .fill-page 接上整屏高度链 -->
<div class="view fill-page">
  <SmartTable
    :columns="columns"
    :data="filteredTree"
    :loading="loading"
    row-key="id"
    :pagination="false"
    flex-height
    virtual-scroll
    :toolbar="{ refresh: false }"
    :expanded-row-keys="expandedKeys"
    @update:expanded-row-keys="(keys: number[]) => (expandedKeys = keys)"
  >
    <template #toolbar>
      <n-input v-model:value="keyword" clearable placeholder="搜索..." style="width: 220px" />
      <n-button quaternary @click="toggleExpandAll">
        {{ allExpanded ? '全部收起' : '全部展开' }}
      </n-button>
      <n-button type="primary" @click="openAdd(0)">新增</n-button>
    </template>
  </SmartTable>
</div>

<style scoped>
/* display / flex-direction / 整屏高度链都在 styles/layout.css 的 .fill-page,这里只留卡片间距。 */
.view {
  gap: var(--gap-card);
}
</style>
```

## 注意事项

- 模板顶层直接就是 `<SmartTable>` 时连 `.fill-page` 都不用加，自动满屏；只有外面还包了别的块才需要外壳
- `list` 接口返回全量平铺数据（非分页），后端用 `GET list` 而非 `GET page`
- 后端删除需检查 `HasChildren`（有子节点不允许删除）
- 表单中的"上级节点"用 `OrgTreeSelect` 组件，需传 `:exclude-subtree-of="editingId"` 防止选自己/子孙为父（成环）
- NDropdown 适合收纳多个操作（编辑/新增子节点/删除），避免操作列过宽

**参考源码：** `web/packages/admin/src/views/system/org/index.vue`
