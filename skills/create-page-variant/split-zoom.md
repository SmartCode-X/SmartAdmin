# 变体五：上下分栏定高 + 放大（Split 模式）

一个页面有**两块表**（上：主表；下：跟随选中行的明细，明细可能还分页签），要求整屏装得下、页面自身不出滚动条，行多时只在表体内滚，窄屏（1366×768、1280×600 这类笔记本）也要成立；每块表还能单独放大铺满视口看宽表。生产订单 + 工艺路线、打印模板 + 绑定物料是典型场景。

## 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 上下分栏 |
|---|---|---|
| 布局 | 单表自动满屏 | 外壳 `.fill-page` + 容器 `.fill-split`，两栏各吃一半 |
| 高度 | 不用管 | 全靠 CSS 链；**不写 JS 量高度** |
| 表格 | 一个 SmartTable | 上 SmartTable + 下 SmartTable / `n-data-table`（可在 `n-tabs` 里） |
| 放大 | 无 | 每块表各持一份 `useTableZoom()`，`Teleport` 到 body |
| 查询 | 列级 `search: true` | 主表照常；明细多半没有搜索卡片 |

## 高度：只套类名，不算像素

`styles/layout.css` 的形状 5（`.fill-split`）就是为这个页面写的。不要用 JS 量 `.page` 可用高度、除以二再喂给 `:max-height`：同一件事交给 flex 算，窗口变化、页签开关、工具栏换行时都不用重新量，也没有兜底常量可以写错。

```vue
<div class="order-page fill-page">
  <!-- 搜索卡 / 统计条这类按内容高度的块直接放这里,.fill-page > * 默认 flex-shrink:0 不参与瓜分 -->
  <div class="fill-split">
    <!-- 上:主表。SmartTable 可以直接做 .fill-split 的子元素 -->
    <SmartTable :default-page-size="100" ... flex-height virtual-scroll />

    <!-- 下:明细。带页签时给 <n-tabs> 加 .fill-tabs,面板里的表就接上链了 -->
    <div class="fill-main">
      <n-tabs class="fill-tabs" type="line">
        <n-tab-pane name="route" :tab="t('order.route')">
          <SmartTable ... flex-height virtual-scroll />
        </n-tab-pane>
      </n-tabs>
    </div>
  </div>
</div>
```

- 页根不能写 `height: 100%` 自己算：路由外层 `.page-view` 是自动高度，百分比没有基准，会退化成 auto。`.fill-page` 靠 `:has()` 把一屏高度补给 `.page-view`，一行类名就够。
- `.fill-split` 的子项 `flex-basis` 是 0，所以真的均分；要不等分就在栏上覆盖 `style="flex-grow: 2"`。左右分栏加 `.fill-split--row`。
- 链中间夹了 `<n-spin>` 给它加 `.fill-pass`；裸 `n-data-table` 装在自己的卡片里时卡片加 `.fill-card`。**不要在 SmartTable 外面再套 `n-card`**：它自带卡片外观，多套一层只会白吃一圈内边距。
- `<n-tabs>` 别开 `animated`：naive 的切页动画会给面板层写内联 `height` / `maxHeight`，和 flex 链打架。面板层保持 naive 默认的 `overflow: hidden`，不要改成 auto，否则整块跟着滚、表头跑掉；滚动的责任在每张表自己（`flex-height` 后表体在表内出滚动条，表头钉住）。
- 每张表都要 `flex-height` + `virtual-scroll`，缺 `flex-height` 整条链就没有落点。
- 矮屏宁可让页面滚也别把表压成一条缝时，外壳再叠 `.fill-page--soft`，并给栏一个 `min-height`。
- 表内单元格的按钮组换行、固定列实底、选中行 `.row-selected` 都在 `styles/table.css`，页面不写。

## 放大（`useTableZoom` + `.table-zoom`）

`useTableZoom()`（业务模块 `import { useTableZoom } from 'smart-admin-web'`，包内 `#/composables/useTableZoom`）返回 `{ zoomed, toggle, exit }`，自带 Esc 退出、body 滚动锁（引用计数，多实例不互相踩）、卸载还锁。刻意不用原生 Fullscreen API：naive 的下拉 / 气泡 teleport 到 body，原生全屏只渲染被全屏那棵子树，浮层会整个看不见。样式 `.table-zoom` 在全局 `styles/table.css`（`position: fixed; inset: 0`，z-index 50，高于顶栏、低于 naive 浮层）。每块表各持一份实例。

```vue
<Teleport to="body" :disabled="!detailZoom.zoomed.value">
  <div class="detail-wrap" :class="{ 'table-zoom': detailZoom.zoomed.value }">
    <div class="detail-toolbar">
      <span class="detail-title">{{ selected?.name }}</span>
      <n-space :size="8" :wrap="false">
        …查询框 / 操作按钮…
        <n-button size="small" circle quaternary @click="detailZoom.toggle()">
          <template #icon><AppIcon :icon="detailZoom.zoomed.value ? 'ph:corners-in' : 'ph:corners-out'" /></template>
        </n-button>
      </n-space>
    </div>
    <n-data-table class="detail-table" flex-height virtual-scroll … />
  </div>
</Teleport>
```

1. **必须 `Teleport` 到 body**。`.table-zoom` 是 `position: fixed`，而祖先只要有 `backdrop-filter` / `transform`（卡片的玻璃效果就有）就会成为 fixed 元素的包含块——留在卡片里，遮罩只铺满那张卡片，不是视口。用 `:disabled` 让常态仍在原地渲染。
2. **工具栏必须和表格一起进 Teleport 容器**。放大按钮留在 `n-card` 的 `#header-extra` 里的话，放大后 header 被遮罩盖住，还原按钮点不到，只能按 Esc。所以卡片标题也得挪进来：名字在左、控件靠右。
3. **放大态的高度不用算**。`.table-zoom` 自身是 flex 列，`.table-zoom > .n-data-table` 已给 `flex: 1; min-height: 0`，配 `flex-height` 表格自动吃满遮罩。表格外面再包了一层时，要么把那层类名叫 `table-zoom-body`，要么自己写 `flex: 1; min-height: 0`。
4. 按钮做成与 SmartTable 工具栏自带的刷新 / 密度 / 列设置同形态的圆形图标按钮（`circle quaternary`，悬停 tooltip「放大 / 还原」，放大态换收起图标并染主色），放 `#toolbar-right` 或自绘工具栏里都行；不要写带文字的 `n-button secondary`。多页共用时封成一个小组件。

## 注意事项

- 布局这层**只有 e2e 盖得住**：happy-dom 里没有真实布局，`clientHeight` 恒为 0。值得写的四条断言：外层 `.page` 的 `scrollHeight <= clientHeight + 1`；主表宽度铺满整块（`> 90%`）且高度不超过 `75%`；放大后遮罩 `>=` 视口（只有 Teleport 到 body 才成立）；放大后还原按钮仍可见可点（工具栏跟着 Teleport 出来了）。
- 收尾自查：1366×768 与 1280×600 各看一遍页面不出滚动条；从页根到每张表逐层确认 `flex:1; min-height:0`（`.fill-tabs` 已把 `n-tabs` → 面板容器 → `.n-tab-pane` 补齐）；每张表带 `flex-height`；每块表 `Teleport` + `:disabled` 绑了各自的 `zoomed`；退出放大后 `document.body.style.overflow` 已还原；代码里没有写死的外壳高度、`FALLBACK_*` 常量参与运算。

**基建源码：** `web/packages/admin/src/styles/layout.css`（形状 5 与 `.fill-tabs` / `.fill-pass` 的注释写了每条规则为什么存在）、`web/packages/admin/src/composables/useTableZoom.ts`、`web/packages/admin/src/styles/table.css`（`.table-zoom`）。内核未内置示例页；已落地形状 2 / 3 的页面见 `web/COMPONENTS.md`「页面形状与高度链」。
