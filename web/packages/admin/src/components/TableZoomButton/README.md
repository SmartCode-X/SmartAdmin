# TableZoomButton

表格「放大 / 还原」按钮。状态由调用方的 `useTableZoom()` 持有,本组件只管外观与触发。

```vue
<script setup lang="ts">
import { useTableZoom, TableZoomButton } from 'smart-admin-web'

const zoom = useTableZoom()
</script>

<template>
  <Teleport to="body" :disabled="!zoom.zoomed.value">
    <div class="detail-wrap" :class="{ 'table-zoom': zoom.zoomed.value }">
      <div class="detail-toolbar">
        <span>{{ title }}</span>
        <TableZoomButton :zoomed="zoom.zoomed.value" @toggle="zoom.toggle()" />
      </div>
      <n-data-table class="table-zoom-body" flex-height virtual-scroll … />
    </div>
  </Teleport>
</template>
```

## Props / Events

| 名称      | 类型                                       | 说明                                      |
| --------- | ------------------------------------------ | ----------------------------------------- |
| `zoomed`  | `boolean`                                  | 当前是否放大态;换图标 + 染主色            |
| `size`    | `'tiny' \| 'small' \| 'medium' \| 'large'` | 默认 `small`,与 SmartTable 工具栏按钮同档 |
| `@toggle` | —                                          | 点击;调用方接 `zoom.toggle()`             |

## 约定

- **形态固定**:`circle quaternary` + tooltip,与工具栏自带的刷新 / 密度 / 列设置一致。不要改成带文字的 `secondary` 按钮。
- **必须和表格一起进 `Teleport` 容器**。留在 `n-card` 的 `#header-extra` 里,放大后 header 被遮罩盖住,还原按钮点不到,只能按 Esc。
- 放遮罩自绘工具栏或 SmartTable 的 `#toolbar-right` 都行;每块表各持一份 `useTableZoom()` 实例。

文案键 `table.zoom` / `table.zoomExit`。完整页面骨架见 `skills/create-page-variant/split-zoom.md`。
