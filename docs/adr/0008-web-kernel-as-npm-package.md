# ADR 0008 — 前端内核以 npm 包分发:单包、预编译、插件式扩展

- 状态:已采纳(2026-09-12)
- 相关:[[ADR-0007]](品牌 SVG 仍不进 NuGet,随前端包分发);实现见 `web/packages/admin/src/createSmartAdmin.ts`、`web/packages/admin/src/index.ts`(公开 API 面)与 `web/template/`

## 背景

后端是 NuGet 包,消费方升级 = 改 `Directory.Packages.props` 里的版本号。前端 `web/` 却是整目录 degit / fork,靠一份文件归属清单压低合并冲突:内核文件上游写、消费方不写,消费方的东西一律进新文件。

这套约定在真实下游上的表现:

- 在内核之上再做一层私有扩展的下游(自带后端 NuGet 包和前端页面):`web/` 以 git subtree 嵌着内核的 `web/`,每次升级是一次 subtree 合并,还要求工作区干净。
- 直接做业务的下游应用:与内核没有 git 关系,升级是以某个基线做三方合并,再逐文件按归属清单覆盖。它有意覆盖的内核文件只有一个:`components/SmartLogo.vue`。
- 业务应用再接入那层扩展时,要同时跟两个上游同步前端,而扩展层的 `web/` 里又套着内核的副本,合并成本随下游的数量与层级相乘。

前后端必须一起改的变更并不少见(10.4.0 的权限按钮收拢与菜单 Id 重排就是)。后端一个版本号能表达的对齐关系,前端只能靠「合到哪个 tag」来表达。

维护面上,上游与已知下游是同一个维护者。下游缺什么,直接做进内核当配置项,比在下游改内核文件更省。包分发最大的代价「消费方不能随意改内置页」,在这个前提下几乎不存在。

## 决策

### 一、内核前端整体发成一个 npm 包 `smart-admin-web`,与 NuGet 元包同号

包的边界 = 除应用自己的业务页之外的一切:布局、路由与动态路由重建、stores、API 客户端与内核端点、共享组件、composables、样式与主题、语言包、指令,以及全部内置页面(`views/system/**`、`views/personal/**`、`views/login/**`、`views/module`、`views/error`、`views/embed`、`views/mfa`、`views/oauth`、`views/dashboard`)。

不做「只打页面包」:以 `views/system/user/index.vue` 为例,它从 components、composables、api、stores、utils、types 六个目录导入,其余系统页同形。页面进包而 stores 不进,包就得反向 import 消费方的代码,依赖方向反了。

单包而非多包:拆包的三条常见理由(多套 UI 库、模块可选且后端也是独立包、某部分带很重的可选依赖)目前一条都不成立。UI 固定 Naive UI,内置页与 NuGet 元包同进同退,维护人手也撑不起多包的版本矩阵。包只开 `.` 与 `./style.css` 两个入口,公开 API 集中在 `src/index.ts` 一处具名导出;确有需要时再加子路径导出。

包名不带 scope,与同账号下的 `smart-naive-table`、`smart-naive-icon` 同一风格;`smart-admin`、`smartadmin` 已被占用。

以后拆包只认一条原则:**前端包的边界跟着后端包的边界走。** 在内核之上另有后端 NuGet 包的扩展层,它的前端就是另一个 npm 包,与它自己的 NuGet 包同号发布,不塞进内核包。

### 二、预编译分发,不发 `.vue` 源码

Vite 库模式输出 ESM(`preserveModules`,内置页各自保持懒加载)+ `.d.ts` + 一份 `style.css`,带 sourcemap。发源码要靠消费方的 Vite 配置(别名、`optimizeDeps.exclude`)才能跑,边界不清;预编译的边界最硬,也最符合「消费方不改内置页」的意图。

随之而来的三条约束:

- 包内不写 `@/`:进了包,`@` 会解析到消费方自己的 `src`。包内私有别名是 `#/` → `src/`,由 Vite alias 与 tsconfig paths 解析,构建后成为相对路径。不走 `package.json` 的 `imports`,Node 不接受以 `#/` 开头的子路径。
- 七处 `import.meta.glob` 接缝(页面表、详情路由、静态路由、语言包扩展、菜单标题映射、本地 SVG、品牌图)预编译后看不到消费方的文件。包内只 glob 自己的文件;消费方在自己的代码里写 glob,经初始化函数传入。
- `import.meta.env` 在库构建时被静态替换,API 根地址、版本号、开发态改为运行期配置(`lib/runtime.ts`),由初始化函数写入;API 客户端跟随 `runtime.apiBase` 懒重建,模块求值早于初始化也拿得到正确地址。

### 三、初始化函数 + 插件接口,应用本身也只是一个插件

```ts
createSmartAdmin({
  plugins: [extensionPlugin], // 顺序即覆盖优先级:后者覆盖前者
  views: import.meta.glob('./views/**/*.vue'), // 同名 key 覆盖内置页
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  routes: [...],
  brand: { logo: MyLogo, title: 'XX' }, // 品牌默认值:sys.site.* 配置为空时使用
}).mount('#app')
```

- 页面 key 沿用菜单 `component` 字段的字符串(`system/user/index`),即 `views/` 之后去掉 `.vue` 的路径。内核用 `system/*` 等前缀,扩展层与应用各用自己的前缀,只有要覆盖时才故意同名。覆盖顺序 = 应用 > 插件(按数组顺序)> 内核。结果与后端相同(应用压过内核),机制相反:前端是后注册的覆盖先注册的,后端 `TryAdd` 是消费方先注册的胜出。`<模块>/detail.vue` 按约定生成 `/<模块>/:id/detail` 详情路由。
- 插件 = `{ views, locales, routes, menuTitles, icons, install }` 同形对象。需要 app 实例的注册放 `install`;三个登记表(`registerMenuBadge`、`registerHeaderTool`、`onRealtime`)作为包的公开导出保留,扩展层用它们挂菜单角标、顶栏入口与实时事件。
- 菜单管理表单的「组件路径」下拉从合并后的页面表取值,内核、插件、应用的 key 都在。
- 扩展层导出给业务页嵌用的组件,按公开 API 对待,改签名即破坏性变更。

### 四、必须单实例的依赖声明为 peerDependencies

vue、vue-router、pinia、vue-i18n、naive-ui、`@iconify/vue`、`@vueuse/core`、`smart-naive-table`、`smart-naive-icon`。判据:凡持有全局单例(app 实例、injection key、图标注册表)的,都必须与消费方共用一份,否则会出现两份 pinia / router / `SMART_TABLE_DEFAULTS`,权限与路由无声失效。扩展包对内核包同样是 peer,版本范围与它的 NuGet 包对内核的依赖范围一致:对不上时 `npm install` 直接报错,作用等同 NuGet 的依赖检查。

### 五、API 类型分层随包走

内核包自带内核端点的 `paths` 类型(导出为 `KernelPaths`);扩展包自带自己端点的类型;应用对着自己的后端生成一份,里面同时有内核端点和自己的端点。`createApiClient<P>()` 为任意 `paths` 建客户端,中间件链(超时、Bearer + CSRF、401 刷新重放、40024 再认证)与内核客户端是同一条。

### 六、品牌:`SmartLogo` 消费 `sys.site.logo`,不再是消费方覆盖点

登录页、登录分栏皮肤、侧栏、顶栏、应用选择页五个调用点一律 `<SmartLogo>`,由它按「`sys.site.logo` 有值用图,否则用初始化项 `brand.logo`,都没有才是内置 SVG」渲染。下游只需在 `sys.site.logo`(或自己的配置种子)里填图片路径,或在代码里给 `brand.logo`。

### 七、仓库与发布

- `web/` 是 npm workspace:`web/packages/admin`(包)+ `web/template`(薄壳应用,即外部消费方 degit 的起点,也是内置页 e2e 的宿主;dev 时直接走内核源码)。
- `release.yml` 在同一个 `v*` tag 上发 NuGet 与 npm,两边都走 Trusted Publishing(GitHub OIDC),仓库不存任何发布密钥。npm 只能给已存在的包建信任关系,包的首个版本手动发布。
- 未发布的内核改动要在下游联调:`npm pack` 出 tgz 本地安装,与 `templates/smoke-test.ps1` 把 nupkg 打到本地源同理。
- 私有扩展包由扩展方自选私有源,npm 包与它的 NuGet 包放同一个源,前后端共用一套凭据。GitHub Packages 的 npm 源要求包名带所有者 scope。

## 被否方案

- **维持 fork / subtree + 文件归属清单。** 两层下游(业务应用 → 扩展层 → 内核)下合并成本相乘;前端没有版本号可对齐。
- **只打页面包(system/dict/config 等)。** 页面依赖整个内核,依赖方向会反。
- **一开始就拆多包(core / layout / pages-system…)。** 见决策一,三条拆包理由都不成立;版本矩阵与 peer 协调是实打实的负担。以后按「跟后端包边界走」再拆。
- **发 `.vue` 源码包(不预编译)。** 跑起来依赖消费方的 Vite 配置,边界软;与「不让改内置页」的意图相悖。
- **git submodule。** 只是 subtree 换个形,升级仍是 git 操作而非版本号,且对与内核没有 git 关系的下游无解。
- **微前端 / Module Federation。** 解决的是多团队独立部署;这里各层是同一维护者、同一构建,引入运行时耦合没有收益。

## 后果

- 前后端升级都只改版本号,`Directory.Packages.props` 与 `package.json` 填同一个数字。下游仓库不再装内核源码,内核的测试与 lint 也不再挤进下游。
- 修复到下游必须经发版,前端修订版要发得更勤;不再有「合到任意 commit」的自由。
- 下游要微调内置页只有两条路:把需求做进内核当配置项(上下游同一维护者时首选),或整页复制到自己的 `views/`、用同名 key 覆盖。后者这一页从此不随内核升级,代价与后端 `replace-service` 相同。「不让改」只是默认不改,拦不住 patch-package,真正的约束是改了就得自己维护。
- 对已 fork `web/` 的外部消费方是破坏性变更:目录结构变了,之后合并上游会很难。继续用旧 fork 不会坏,但要拿后续升级得切到包;迁移步骤写在 `CHANGELOG.md` 对应版本段落。
- 包里带 CSS 导入,消费方的 vitest 要把 `smart-admin-web`、`smart-naive-table`、`smart-naive-icon`、`md-editor-v3` 列进 `server.deps.inline`。

## 非目标

eject 工具(把内置页源码复制到消费方项目)、插槽级页面扩展(给内置页加列 / 加字段)、多 UI 库。前两项等真有需求再做。

## 验证

采纳前在一个真实下游上验证了两点:① 页面表改由初始化函数传入后,菜单动态路由、F5 重建、`MissingRoute` 诊断、`namedPage` 以 loader 引用为键的 keep-alive 失效条件都还成立;② 预编译包 + peer 依赖下,应用里的 pinia / router / `SMART_TABLE_DEFAULTS` 确实只有一份。内核的 e2e 在 `web/template` 上照跑。
