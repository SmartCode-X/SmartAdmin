# 变体二：主从/左右分栏（Dict 模式）

适用于父子关系紧密的数据对（字典类型+字典项、分类+子项）。

## 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 主从分栏 |
|---|---|---|
| 布局 | 单表 | 左右两栏（flex wrap） |
| 数据关系 | 独立 | 左侧选中 → 右侧按选中加载 |
| 表格 | 一个 SmartTable | 左 SmartTable + 右 n-data-table |
| 批量删除 | 一组 useBatchDelete | 两组（主/从各一组） |
| CRUD 状态 | 一套 show/form/editingId | 两套（主/从各一套） |
| 缓存 | 无 | 改动后需 `dictStore.invalidate()` 刷缓存 |

## 关键代码模式

```typescript
// 主从选中态
const selectedType = ref<SysDictType | null>(null)
const items = ref<SysDictItem[]>([])

async function loadItems() {
  const code = selectedType.value?.code
  if (!code) { items.value = []; return }
  itemsLoading.value = true
  try {
    const list = await dictAdminApi.items(code)
    // 竞态守卫：await 期间用户可能已切换，过期响应不覆盖
    if (selectedType.value?.code === code) items.value = list
  } finally {
    if (selectedType.value?.code === code) itemsLoading.value = false
  }
}
async function selectType(r: SysDictType) {
  selectedType.value = r
  await loadItems()
}

// 两组独立的批量删除
const { checkedKeys: typeCheckedKeys, hasSelection: typeHasSelection, run: typeBatchDelete } = useBatchDelete({
  remove: dictAdminApi.typeBatchRemove,
  refresh: () => { selectedType.value = null; items.value = []; typeTableRef.value?.refresh() },
})
const { checkedKeys: itemCheckedKeys, hasSelection: itemHasSelection, run: itemBatchDelete } = useBatchDelete({
  remove: dictAdminApi.itemBatchRemove,
  refresh: () => loadItems(),
})
```

## Template 骨架

```vue
<!-- 外壳套 .side-page 接整屏高度;两栏是 n-card 时卡片加 .fill-card、内容层 content-class="fill-main",
     SmartTable 才能拿到基准高度。.fill-main 别写在 <SmartTable> 上(inheritAttrs:false,class 到不了根节点)。 -->
<div class="master-detail-layout side-page">
  <!-- 左：主表 -->
  <n-card class="pane fill-card" content-class="fill-main">
    <SmartTable
      :default-page-size="100"
      :columns="typeColumns"
      :fetcher="api.typePage"
      flex-height
      virtual-scroll
      :search="{ layout: 'inline' }"
      :active-row-key="selectedType?.id ?? null"
      :row-props="() => ({ style: 'cursor: pointer' })"
      @row-click="(row) => selectType(row)"
    />
  </n-card>

  <!-- 右：从表（选中后显示）-->
  <n-card v-if="selectedType" class="pane fill-card" :title="`${selectedType.name} 的子项`">
    <n-data-table :columns="itemColumns" :data="items" :loading="itemsLoading" flex-height virtual-scroll />
  </n-card>
  <n-card v-else class="pane fill-card">
    <n-empty description="请先选择左侧项" />
  </n-card>
</div>

<style scoped>
/* display / 拉伸 / 整屏高度都在 styles/layout.css 的 .side-page,这里只留换行与间距。 */
.master-detail-layout {
  flex-wrap: wrap;
  gap: var(--gap-card);
}
.pane {
  flex: 1 1 380px;
  min-width: 0;
}
</style>
```

## 注意事项

- 左侧用 `search: { layout: 'inline' }` 压缩搜索栏（窄面板无空间放搜索卡片）
- `:active-row-key` 高亮选中行，点击行触发 `@row-click`
- 右侧操作按钮中的 `stopPropagation` 防止列内按钮点击冒泡触发 `@row-click`
- 竞态守卫：`loadItems` 中 await 回来后要验证选中状态没变
- 如果主从数据影响全局缓存（如字典），每次写操作后调 `invalidate()`

**参考源码：** `web/packages/admin/src/views/system/dict/index.vue`
