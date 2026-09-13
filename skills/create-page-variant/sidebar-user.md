# 变体三：侧栏筛选（User 模式）

适用于表格需要额外分类筛选维度的页面（用户按机构筛选、订单按客户筛选）。

## 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 侧栏筛选 |
|---|---|---|
| 布局 | 单表 | 左侧树/列表 + 右侧 SmartTable |
| 表格参数 | 固定 | `computed` 动态参数，响应侧栏选中变化 |
| 关联下拉 | 无 | `onMounted` 预加载多个下拉选项 |
| 跨页导航 | 无 | watch `route.query` 响应外部跳入参数 |
| 新增/编辑 | 单表单 | 表单字段多，可能需要 NGrid 多列布局 |

## 关键代码模式

```typescript
// 左侧机构树
const orgTree = ref<Tree<SysOrg>[]>([])
const selectedOrgId = ref<number | null>(null)
const tableParams = computed(() =>
  selectedOrgId.value == null ? {} : { orgId: selectedOrgId.value },
)

// 预加载关联下拉
onMounted(async () => {
  try {
    const { items } = await positionApi.page({ page: 1, pageSize: 200 })
    positionOptions.value = items.map((p) => ({ label: p.name, value: p.id }))
  } catch { /* 静默：配角下拉失败不打断列表 */ }

  try {
    orgTree.value = buildTree(await orgApi.list())
  } catch { /* 静默 */ }
})

// 跨页导航：角色页跳来 ?roleId=123
const route = useRoute()
watch(
  [tableRef, () => route.query.roleId],
  ([inst, roleId]) => {
    if (!inst) return
    const next = roleId == null ? undefined : Number(roleId)
    if (inst.params.roleId === next) return
    inst.params.roleId = next
    inst.search()  // 回第 1 页重查
  },
  { immediate: true },
)
```

## Template 骨架

```vue
<!-- 「左分组栏 + 右列表」= styles/layout.css 的形状 3:.side-page 负责 display / gap / 拉伸 / 整屏高度,
     直接子元素的 SmartTable 自动吃满剩余宽高,页面只定侧栏宽度。 -->
<div class="sidebar-layout side-page">
  <!-- 左侧筛选树:用 n-card 时加 .fill-card 让树吃满卡片剩余高度、自己竖向滚 -->
  <n-card class="sidebar fill-card" :bordered="false" size="small">
    <n-tree
      :data="orgTree"
      :selected-keys="selectedOrgId == null ? [] : [selectedOrgId]"
      key-field="id"
      label-field="name"
      @update:selected-keys="onOrgSelect"
    />
  </n-card>

  <!-- 右侧表格:是 .side-page 的直接子元素,不要再包一层 div -->
  <SmartTable
    ref="tableRef"
    :default-page-size="100"
    :columns="columns"
    :fetcher="userApi.page"
    :params="tableParams"
    flex-height
    virtual-scroll
  >
    <template #toolbar>
      <n-button type="primary" @click="openAdd">新增</n-button>
      <n-button type="error" :disabled="!hasSelection" @click="batchDelete">
        批量删除
      </n-button>
    </template>
  </SmartTable>
</div>

<style scoped>
/* 左树 + 右表:display / gap / 拉伸 / 整屏高度都在 .side-page,这里只定侧栏宽度。 */
.sidebar {
  flex: 0 0 200px;
}
.sidebar :deep(.n-tree) {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
</style>
```

不想用 `n-card` 当侧栏时，`styles/layout.css` 还带一套侧栏面板外观：外层 `.side-filter`（宽度按页覆盖 `--side-filter-width`）、头部 `.side-filter__head` + `.side-filter__actions`、滚动区 `.side-filter__body`、平铺分类的行 `.side-row`（选中态 `.is-active`，可带 `.side-row__count` 角标）、树形分类给 `<n-tree>` 加 `.side-tree`。面板不放标题（紧挨着的第一行就是「全部」，再写一遍分类名是重复），头部只留操作按钮；树形分类进入页面默认全部折叠。

## 注意事项

- `tableParams` 是 computed，侧栏选中变化时 SmartTable 自动回第 1 页重查
- 预加载下拉用 `try/catch` 静默失败——配角数据拉不到不应阻断主列表
- 跨页导航需 `watch` 而非 `onMounted`：页面被 keep-alive 时 onMounted 不再触发，只有 query 变化触发 watch
- 同时 watch `tableRef`：首次加载时表格实例可能还没挂载，需要等实例就绪后再设参数
- 新增/编辑的 Input 类型可能不同（`AddUserInput` vs `UpdateUserInput`），根据业务需要决定是否拆分

**参考源码：** `web/packages/admin/src/views/system/user/index.vue`
