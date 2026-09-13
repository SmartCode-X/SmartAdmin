# 组件使用约定

> 约定:后台不设组件演示菜单,组件用法统一沉淀在本文件。完整 API 见各包 README。
>
> **怎么导入**:应用里一律从包入口具名导入,`import { FormContainer, useConfirm, translateError } from 'smart-admin-web'`,SmartTable 从 `smart-naive-table` 导入;本文件提到的组件、composable、工具函数都在 `web/packages/admin/src/index.ts` 导出。内核包自己的页面用包内别名导入同一个文件,如 `import FormContainer from '#/components/FormContainer/index.vue'`。
> 文中的源码路径(`src/...`、`composables/...`)都相对内核包 `web/packages/admin/`。

## SmartTable(smart-naive-table)

列驱动表格:`columns` 数组同时驱动搜索表单、字典渲染与列设置;`fetcher` 是唯一后端契约。
完整文档:https://github.com/SmartCode-X/smart-naive-table/blob/main/README.md

SmartAdmin 内接入约定:

- **fetcher**:直接传 `xxxApi.page`,api 层负责把后端 `PagedList{current,size,total,items}` 归一成 `{items,total}`、把 `{page,pageSize}` 映射成 `{Current,Size}`(见 `src/api/index.ts` 的 `userApi.page`)。
- **labels**:**全局注入,页面不手传**。`createSmartAdmin` 里 `app.provide(SMART_TABLE_DEFAULTS, createSmartTableDefaults({ labels: computed(...) }))` 一次接上 i18n(键在 locale 的 `common.*`/`app.*`/`table.*`),切语言即时生效。要覆盖单页文案才传 `:labels`。全局密度与每页条数选项由应用经 `createSmartAdmin({ table: { density, pageSizes } })` 调整;别在应用里再 provide 一次 `SMART_TABLE_DEFAULTS`,inject 取最近的一份、不合并,会把内核注入的 labels 整份挡掉。
- **列标题必须函数形式** `title: () => t('...')`,切语言即时生效;写成 `title: t('...')` 只在建列那一刻求值,切语言不更新。
- **错误处理留在视图层**:`@error="(e) => message.error(translateError(e))"`,包内不弹 UI。
- **storage-key 命名**:`{模块}-{页面}`,如 `sys-user`;列设置与密度按此键持久化到 localStorage(`protable:` 前缀)。
- **树形页(org / menu)**:静态数据模式 —— `:data="tree"`(行带 `children`)+ `row-key="id"` + `:pagination="false"`,树列设 `align:'left'`;新增/筛选/搜索控件放 `#toolbar`。
  - **列的取舍**:树列 `minWidth:220 + fixed:'left'`、操作列 `fixed:'right'`(`scrollX` 由包内按 `sum(width ?? minWidth ?? 120)` 自动算并绑给 `n-data-table`,无需手传 `scroll-x`);文本列一律 `ellipsis:{tooltip:true}`,否则长路径换行会把行撑高、行高参差。**操作最多留 2 个**(编辑 + `n-dropdown` 更多▾),4 个平铺在 260~300px 里必换行且横向滚动时够不着,org/menu 两页都按这条做。下拉项里的删除用 `useConfirm().confirm`(dialog),`n-popconfirm` 是内联触发器,塞不进 dropdown。
  - **别加恒空列**:菜单树剥掉按钮后只剩目录/页面,而权限码只挂按钮 → 「权限码」列 100% 是「—」。同理关键字过滤跑在剥离后的树上,写 `n.permission` 永不命中,要按权限码搜得查节点的按钮子节点(见 `menu/index.vue` 的 `buttonInfoById`)。
  - **搜索自己算**:静态 `:data` 模式下 SmartTable **不做任何前端过滤**(列的 `search` 配置只渲染搜索表单 + emit),所以关键字过滤走 `computed` + `utils/tree.ts` 的 `filterTree`(命中节点保留整棵子树,未命中但有后代命中的节点作为祖先链保留)。关键字放 `#toolbar` 的 `n-input`,别用 `search` 列配置——树表没分页,搜索卡片白占一整块高度。
  - **展开要受控**:`:expanded-row-keys` + `@update:expanded-row-keys`(不是 SmartTable 的 prop,靠 `inheritAttrs:false + v-bind="attrs"` 透传给 `n-data-table`,和 `:loading` 同理)。**受控后必须删掉 `default-expand-all`** —— naive 里受控值优先,两者并存会让初始 `[]` 把"默认全展开"直接覆盖成全折叠;"全展开"改由 `expandableIds(tree)` 播种。data 变了受控 keys 不会自动跟着变,搜索后要重算,否则命中结果藏在折叠的祖先里。
  - **行内写值的坑**:`filterTree` 剪枝时,"仅因后代命中而保留"的祖先是浅拷贝。搜索态下往行对象上写值(`r.enabled = v`)写的是副本,不回源树 → 开关会弹回去。行内变更后**重拉**(`load()`)而不是本地写回;`StatusSwitch` 是悲观更新(请求成功才 emit),重拉即最终态。
- **主从选中(dict)**:`:active-row-key` + `@row-click` 做行高亮/选中(勿再 `:deep(> td)`);行内交互控件(开关/按钮)的 render 里要 `stopPropagation`,否则点它会冒泡触发 `@row-click`。
- **窄栏搜索(dict)**:`:search="{ layout: 'inline' }"` 无卡片单行,配合列 `search: true`。
- **排序(user)**:列写 `sorter: true` → 点表头把 `{ sortField, sortOrder }` 并进 fetcher;**api 层要把它们映射成后端 `SortField/SortOrder` query**(见 `userApi.page`/`positionApi.page`)。后端按实体列白名单安全排序(非法字段忽略回退默认,`PagedListExtensions.OrderBySafe`),字段名 = 实体属性名(大小写不敏感)。
- **行排序(position)**:岗位顺序用**可编辑 `Sort` 字段**——编辑弹窗一个 `n-input-number`,列表默认 `OrderBy(Sort)`,用户表单的岗位下拉也继承此序。SmartTable 本身有 `row-draggable`(`drag-handle` + `@row-drag-sort`,sortablejs 懒加载)这个能力,但本项目**未接线**:没有 `positionApi.reorder`,后端也无 `POST /sys/position/reorder` 端点。要真拖拽排序,需先补该端点(按序赋 Sort)再启用手柄——对一个极少改动的小列表,数字框已够,故未做。
- **别用 scoped 样式去调 SmartTable 内部**:包内 `inheritAttrs:false`,`class` 落在内层 `n-data-table` 上,而 scope id 落在 SmartTable 自己的根元素上,`.x :deep(.y)` 要求两者在同一元素,**永不命中**(比如拿它写 `min-height:0` 治横向滚动、写 `padding` 压空态高度,都不生效)。调内部样式走 `:theme-overrides`(经 attrs 透传给 `n-data-table`,只影响这一张表,如 `:theme-overrides="{ emptyPadding: '16px 0' }"`);实在要写 CSS 就用不带类名前缀的 `:deep(.y)`——编译成 `[data-v-xxx] .y`,起点是 SmartTable 根元素,能命中。
- **搜索折叠**:`:search="{ collapsible: true }"`(仅 grid 布局,搜索项多时才用)。
- **逃生口 slot**:`#toolbar-right`(工具栏右侧)、`#header-{key}`、`#empty`、`#pagination-prefix`;全局默认(align/pageSizes/emptyText/tag 等)都在 `createSmartAdmin` 注入的那份 `SMART_TABLE_DEFAULTS` 里,应用能调的见上面 labels 一条。
- **已能用(透传)**:列宽拖拽(列 `resizable`)、虚拟滚动(`:virtual-scroll`+`max-height`)、合计行(`:summary`)、合并单元格(列 `rowSpan/colSpan`)——经 attrs/列透传,无需新 API。
- **版本**:依赖 `^2.0.0`(peer);列排序依赖后端 `SortField/SortOrder`(行拖拽是纯前端能力,本项目未接线,见上),改后端后 `npm run gen:api` 重生成 schema。

范例页:`src/views/system/user/index.vue`(标准列表 + 排序)、`position`(可编辑 Sort 排序)、`org`/`menu`(树)、`dict`(主从 + inline 搜索)。

## 自研通用组件索引

**每组件详细 API 见其目录 README**;新通用组件一律「目录 + `index.vue` + `README.md`」;composable/store 的说明写在源码头注释,本文件只留索引与一句话定位。

| 组件 | 定位 | README |
|---|---|---|
| FormContainer | 弹窗/抽屉二合一表单容器;onConfirm 协议接管 loading/关闭,全局形态可在设置抽屉切换 | `src/components/FormContainer/README.md` |
| StatusSwitch | 表格行内启停开关;悲观更新,失败自动回滚 | `src/components/StatusSwitch/README.md` |
| DictSelect | 字典下拉,`$attrs` 全透传 n-select | `src/components/DictSelect/README.md` |
| DictTag | 表格列字典翻译 + 语义色标签 | `src/components/DictTag/README.md` |
| OrgTreeSelect | 机构树下拉;拉 `org/list` 平铺 → `utils/tree.buildTree` 拼树,`$attrs` 透传 n-tree-select;`excludeSubtreeOf` 剪自身子树防成环 | `src/components/OrgTreeSelect/README.md` |
| FileUpload | 封 n-upload `custom-request`;内部 `fileApi.upload` 自动带 Bearer,成功 `emit('uploaded', out)`,上传起止各 `emit('loadingChange', bool)`(try/finally 兜底,给触发器加"上传中"spinner);`$attrs` 透传(accept/multiple/show-file-list)。`chunked` 走分片/断点续传/秒传(`utils/chunkUpload`,进度回 n-upload) | `src/components/FileUpload/README.md` |
| PasswordStrength | 密码强度条 + 规则清单;自包含,`:value` 传密码明文,内部拉当前生效密码策略动态构建规则(改密页 / 建用户页共用) | `src/components/PasswordStrength/README.md` |
| ApiSelect | 通用远程分页下拉基座;`fetch(keyword)` 回归一选项,管加载/远程搜索防抖/竞态/loading,`$attrs` 透传 n-select | `src/components/ApiSelect/README.md` |
| UserSelect | 人员选择器;基于 ApiSelect,`userApi.page` 搜索 + 可选 `orgId` 部门过滤,label 为「姓名(账号)」 | `src/components/UserSelect/README.md` |
| MarkdownEditor / MarkdownView | 通知公告 Markdown 编辑/只读渲染(封 md-editor-v3);存 Markdown 纯文本,跟随明暗主题;正文里以单个 `/` 开头的站内链接走 vue-router(不整页刷新,Ctrl/Cmd + 点击仍新开标签),http(s) 外链新标签打开;通知页已落地 | `src/components/MarkdownEditor/README.md` |
| Chart(+ LineChart/BarChart/PieChart) | ECharts 封装(封 vue-echarts);自动跟随明暗主题/accent、按需注册图种、自带 autoresize;预设传 data、BaseChart 传 option;工作台已落地 | `src/components/Chart/README.md` |
| CodeBlock | 代码/JSON 只读展示;NCode + `hljs/lib/core` 按需注册(现仅 json),复制按钮 + 自动换行,配色随 Naive 主题;操作日志详情已落地 | `src/components/CodeBlock/README.md` |
| DetailPage | 详情页外壳:返回 + 标题 + actions/body 插槽;`@back` 交父级(路由态关标签回列表 / 就地态清状态),补偿非菜单详情路由的空面包屑;配 `useTabTitle` 设动态标签标题。用法/骨架见 `skills/create-page-variant.md` 变体四 | `src/components/DetailPage/README.md` |
| ImportWizard | 导入四步向导(`n-steps`):上传 → 列映射 → 预览改错(**裸 `n-data-table`**,可编辑+错误红底 tooltip)→ 结果;api 注入,用户管理已落地。**「已存在」(46010)按重复策略呈现**,不是硬错误 —— 判定在 `src/utils/importDup.ts` | `src/components/ImportWizard/README.md` |
| ExportColumnsModal | 导出选列弹窗;默认按 `defaultSelected` 勾选,确认后父级带 **SmartTable 当前筛选** 请求 blob 下载;用户管理 / 操作日志已落地 | `src/components/ExportColumnsModal/README.md` |
| CronEditor | 6 段 cron 可视化编辑(秒/分/时/日/月/周 + 表达式直填,日 L/L-n/nW/LW、周 nL/n#m 专项);日/周互斥自动落 `?`,防抖 400ms 调 preview-cron 预览未来时刻;定时任务表单已落地 | `src/components/CronEditor/README.md` |
| DictRadio / DictCheckbox | 字典单选(按钮组)/ 字典多选(复选框组);`typeCode` 取数经 `stores/dict` 缓存,其余 `$attrs` 透传 n-radio-group / n-checkbox-group;与 DictSelect 同源同范式 | `src/components/DictRadio/README.md`、`src/components/DictCheckbox/README.md` |
| RoleSelect | 角色选择器;基于 ApiSelect,`roleApi.page` 名称搜索,只列启用角色,value=角色 id,多选经 `$attrs` | `src/components/RoleSelect/README.md` |
| JsonEditor | JSON 值编辑:textarea + 实时校验 + 一键格式化,零依赖;只给约定为 JSON 的配置字段用 | `src/components/JsonEditor/README.md` |
| SelectTable | 弹窗表格选择器;单选/多选 + 搜索字段 / 关键字模式,`value/selected` 双模型回传主键与行对象;`:paginated="false"` 给小档案一次拉全 | `src/components/SelectTable/README.md` |
| UserPicker | 授权用户选择器;机构树过滤 + 用户表格多选,确认后回传用户 Id 数组 | `src/components/UserPicker/README.md` |
| ErrorBoundary | 内容区渲染错误兜底;`onErrorCaptured` + 重试/回首页,chunk 失效自动重载一次。已包住 `layouts/default.vue` 的 `router-view`,页面作者不必再手动套 | `src/components/ErrorBoundary/README.md` |
| TableZoomButton | 表格「放大/还原」按钮;形态与 SmartTable 工具栏按钮一致,状态由调用方的 `useTableZoom()` 持有,必须与表格一起进 `Teleport` | `src/components/TableZoomButton/README.md` |

字典三件套的数据基座是 `src/stores/dict.ts`(按 typeCode 缓存 + 并发去重;字典管理操作后调 `invalidate()`),页面拿原始选项用 `useDictOptions(typeCode)`。范例页:`src/views/system/menu/index.vue`、`module/index.vue`(FormContainer + useConfirm + StatusSwitch 完整落地)。

## 页面标题与菜单标题(composable / 单源)

- `composables/usePageTitle.ts`:App.vue 调一次,`document.title` 随路由变(多标签页的动态标题优先),`<html lang>` 跟随当前语言。
- `locales/menuTitle.ts` 的 `translateMenuTitle(title, path)` 是**菜单标题翻译的单源**:侧栏 / 面包屑 / 全局搜索 / 多标签 / 浏览器标题必须走它(规则=标题含 `.` 视为 i18n key),否则同一个菜单能在四个地方显示成四个样子。

## 通用格式化(utils/format.ts)

`fmtDateTime(值, { seconds?, empty? })` / `fmtBytes(字节)` / `operatorText(行, empty?)` —— 时间只做截断不走 `new Date()`
(后端下发的是服务器本地时区的 ISO 串,交给 Date 会被当 UTC 平移几小时)。写页面时别再各写一遍:
各写各的字节格式化容易长出两套口径,同一个数在文件页与监控页显示不一样。

## useConfirm(二次确认 + 结果 toast,composable)

`src/composables/useConfirm.ts`,**仅限 setup 中调用**(依赖 Dialog/Message Provider)。`const { ask, confirm, run } = useConfirm()`:

- `run(action, successMsg?) => Promise<boolean>`:执行 → 成/败 toast。配模板层 `n-popconfirm` 用(popconfirm 当触发器,后半段交给 run):
  ```ts
  onPositiveClick: () => run(() => xxApi.remove(r.id), t('xx.deleted')).then((ok) => { if (ok) load() })
  ```
- `confirm({ content, title?, type?, action, successMsg? }) => Promise<boolean>`:先弹 dialog、**action 在 dialog 挂起期间执行**(确认钮 loading,执行中锁死取消/Esc/遮罩,防连点重复执行);给不适合内联的重操作(批量删除等)。取消/关闭/Esc/失败均 false;`successMsg: false` 关掉成功 toast。
- `ask({ content, title?, type? }) => Promise<boolean>`:仅确认不执行,给需要自管后续流程的组件用(StatusSwitch 即基于它)。Esc/遮罩/取消均 false。

## useTabTitle(详情页动态标签标题,composable)

`src/composables/useTabTitle.ts`,**仅限 setup 中调用**。`const setTabTitle = useTabTitle()`,详情页数据加载后 `setTabTitle(记录名)` 把当前标签标题改成记录名(如「张三」);内部走 `tabsStore.setTitle` → 置 `titleFixed`,`addTab` 复访时不会用静态 `meta.title` 覆盖,标题随 tab 持久化、F5 无闪复原。**就地态(列表页内切换详情)别调用**——那时 `route.path` 是列表标签,会改错标签。

## 屏幕形态三件套(平板 / 小屏,composable)

平板与小屏适配收成三个内核能力,页面直接用:

- `composables/useCompactScreen.ts`:`useCompactScreen({ maxWidth?, maxHeight? })` → `ComputedRef<boolean>`。判据是**视口尺寸不是 UA** —— 平板横竖屏是同一台设备,认 UA 必然错一半。默认宽 ≤ 900 或高 ≤ 640 算紧凑(iPad 竖屏 768/834、1280×600 矮屏笔记本都命中)。纯函数版 `isCompactSize()` 供非响应式场景。
- `composables/useVisualViewportHeight.ts`:把 `visualViewport.height` 持续写进 `--vvh`(px),供需要避开软键盘的页面写 `height: var(--vvh, 100dvh)`。`dvh` 跟的是布局视口,软键盘弹起时它**不变**,底部按钮会被键盘压住 —— 只有 `visualViewport` 会缩。布局壳已全局调过一次,页面直接用变量即可。
- `composables/useFullscreenToggle.ts`:`{ isSupported, isFullscreen, enter, exit, toggle }`。不用 VueUse 的 `useFullscreen`,因为它在 iPadOS 上把 `isSupported` 判成 false(iPadOS Safari 伪装桌面版,只有 `webkitRequestFullscreen`,没有标准名)。`isSupported === false`(iPhone)时**把按钮藏掉**,别留一个点了没反应的。顶栏的全屏按钮用的就是它。

FormContainer 的 `:fullscreen="true" | 'auto'` 建在 `useCompactScreen` 上,默认 `false`(没传这个 prop 的既有页面不该因为换台设备就换形态)。

## useTableRowSort / useTableZoom(表格行拖拽排序 / 放大,composable)

- `composables/useTableRowSort.ts`:n-data-table 行拖拽排序,sortablejs 懒加载、`forceFallback` + 隐藏浮层把拖拽锁在表内。容器套全局类 `.row-sort`(styles/table.css)、手柄列渲染 `DRAG_HANDLE_CLASS`,`rows()` 原地重排后 `onSorted(row, from, to)` 回调。
- `composables/useTableZoom.ts`:把某块区域临时铺满视口看宽表。刻意不用 Fullscreen API(Naive 浮层 teleport 到 body,原生全屏看不见)。`const { zoomed, toggle } = useTableZoom()`,容器 `:class="{ 'table-zoom': zoomed }"`,Esc 退出,body 滚动锁带引用计数。

## 页面形状与高度链(styles/layout.css,约定式)

满屏列表页整页吃满一屏、滚动只发生在表体里(表格配 `flex-height` + `virtual-scroll`,两者必须成对)。高度链只写在 `styles/layout.css` 一处,页面按形状套类名,不各自抄 `:deep` 链:

1. 模板顶层直接是 `<SmartTable>`:什么都不用加,自动满屏;
2. 顶层是自己的外壳 div:加 `.fill-page`;主表不是直接子元素时,给「包住主表的那一层」加 `.fill-main`;
3. 「左分组栏 + 右列表」:外壳加 `.side-page`(侧栏宽度按页覆盖 `--side-filter-width`),侧栏面板外观用 `.side-filter` / `.side-row` / `.side-tree`;
4. 页签页:`<n-tabs>` 加 `.fill-tabs`;
5. 上下 / 左右分栏均分:容器加 `.fill-split`(横向加 `--row`),每栏加 `.fill-main`。

链中间夹了 `<n-spin>` 给它加 `.fill-pass`;矮屏宁可让页面滚也别把表压扁时外壳再叠 `.fill-page--soft`;裸 `n-data-table` 装在卡片里时卡片加 `.fill-card`。弹窗表单分节用 `.form-section` / `.form-section__title`(修饰 `--fieldset` / `--panel`),别在页面 scoped 里再画一遍标题。表格全局外观(斑马纹 / 列分隔 / 固定列实底 / 选中行 `.row-selected`)在 `styles/table.css`,页面也不必再写。已落地:用户 / 字典(形状 3),菜单 / 机构 / 通知 / 回收站 / 任务监控(形状 2、4)。

## naive-ui 组件必须逐个显式 import

内核包与模板应用都**没有自动导入插件,也没有全局 `app.use(naive)`** —— 模板里用到哪个 `<n-xxx>`,就得在同一个 SFC 的 `<script setup>` 里 `import { NXxx } from 'naive-ui'`(范例:`views/system/position/index.vue` 一次列全)。

**漏了不会编译报错**:未注册组件在 Vue 里是运行时警告,`vue-tsc --noEmit` / `vite build` / `vitest` 一律不查模板里的组件解析。表现是浏览器控制台一行 `Failed to resolve component: n-xxx`,页面上那块**直接空白**——表格不渲染、表单 ref 拿不到实例(于是 `formRef.value?.validate()` 静默跳过),看着就像「点了没反应」。**页面写完必须在浏览器里点一遍**。容易漏的是只在模板里出现、不在 `h()` 里用的那些:`NCard` / `NTabs` / `NTabPane` / `NEmpty` / `NGrid` / `NFormItemGi` / `NDataTable`。

## 外链 / iframe 菜单(约定式,零后端字段新增)

菜单节点(`Type=Menu`)复用现有 `path`/`component` 字段承载,判据是 `isHttpUrl`(`src/utils/url.ts`):
- **外链**:`path` 填 `http(s)://…`、`component` 留空 → 不建路由(`useAuthMenu.buildRoutesForModule` 跳过),点击时 `window.open` 新窗口(`useLayoutMenu.onSelect/onSelectL1` + `MenuSearch.go` 各有 `isHttpUrl` 分支)。
- **内嵌 iframe**:`path` 填内部路径(如 `/embed/docs`)、`component` 填 `http(s)://…` → 注册通用视图 `views/embed/iframe.vue`,URL 进 `meta.iframeSrc`,keep-alive 顺带保住 iframe 状态。
- 菜单管理表单在页面类型下给出 `menu.linkHint` 说明;seed 里同理(`path`/`component` 填 URL 即可)。后端实体/枚举/种子结构不需要为此加字段。

- 布局壳之外的**静态**路由(整屏看板 `/fullscreen/*`、独立打印页)经 `createSmartAdmin({ routes: [...] })` 传入 `RouteRecordRaw[]`,并入顶级静态路由。菜单节点 path 填该路径、component 留空,点击即 `window.open` 新标签(`utils/url.isFullscreenPath`)。

## 数字动画 / 水印(用 Naive 内建,不自研)

- 数字动画:`<span class="tabular"><n-number-animation :from="0" :to="n" show-separator /></span>`;`.tabular` 防滚动抖宽(styles/index.css),前后缀直接写旁边。
- 水印:全局水印已内建(`layouts/default.vue` + 设置抽屉开关,内容默认当前用户名);局部包 `<n-watermark content="…" cross>` 即可。

## 三个登记表:菜单角标 / 顶栏工具 / 实时事件(应用扩展位)

都是**登记表而非插槽**:布局壳(`layouts/default.vue`、`AppHeader.vue`)在包里,应用改不到,登记表让扩展代码留在应用自己的文件里,在 `createSmartAdmin({ install })` 或组件 setup 里调用。三者包内都没有调用方,**别当未使用代码清掉**(同 `docs/coding-standards.md` §2.1)。

- **菜单角标** `composables/useMenuBadge.ts`:`registerMenuBadge('/todo/mine', unreadRef)` → 返回注销函数。按路由 path 登记(不是 `MenuNode` 上的字段——菜单树每次重拉都会把前端算的动态值冲掉);值可传 ref / getter,变了角标自己跟着变。渲染成 `MenuOption.extra` 的 `n-badge`,侧栏 / 顶栏 / 二级 / 移动抽屉四处共用。`0` / `''` / `null` 不渲染(未读为零不留空圈);折叠与 rail 态 n-menu 只画图标、不渲染 `extra`,角标随之隐藏。
- **顶栏工具** `composables/useHeaderTools.ts`:`registerHeaderTool({ key, icon, label, onClick, order?, keepOnMobile? })` 画一个和内置项同款的圆形按钮;要自带角标 / 弹层就换组件模式 `{ key, component }`(内置铃铛就是这形状)。登记项渲染在铃铛左侧,按 `order` 升序,默认窄屏隐藏——顶栏挤不下时右端会被裁,最右的登出入口优先。
- **实时事件** `composables/useRealtime.ts` 的 `onRealtime(event, handler)`:在内置那条 SignalR 连接上挂自己的 hub 事件,返回退订函数。建连前登记的于 `start()` 时统一绑定,之后登记的直接挂上,连接重建自动重绑。**不要另建一条连向 `/hub/realtime` 的连接**:后端按 userId 群发到该用户的全部连接,第二条会把 `notice-changed` / `force-logout` 又收一遍(未读刷两次、强退弹两次)。

## 可加但先不加(设计已备案,别提前造)

- FormContainer size 档位 / `onBeforeClose`;useConfirm 返回结果值版;StatusSwitch 泛型值/乐观模式;DictSelect 展示禁用项;dict 缓存 TTL/SWR;CountTo 自研包装(NNumberAnimation 不够用再说)。

## IconPicker / AppIcon(smart-naive-icon)

离线优先图标选择与渲染:包里的渲染器是 `SmartIcon`、选择器是 `SmartIconPicker`,应用里用内核的封装 `AppIcon`、`IconPicker`。初始化与包装见 `src/lib/icons.ts` 顶部注释、`src/components/IconPicker/index.vue`、`src/components/AppIcon.vue`。应用的本地 SVG 经 `createSmartAdmin({ icons: import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }) })` 注册,在选择器与 `AppIcon` 里以 `local:<文件名>` 使用。
