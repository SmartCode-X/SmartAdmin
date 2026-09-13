# GitHub Copilot 指引 —— SmartAdmin

完整规约在仓库根的 [`CLAUDE.md`](../CLAUDE.md)(同一份内容,`AGENTS.md` 只是指针)。下面是最容易被写错的几条,请当硬约束读。

## 这是什么

SmartAdmin 是**可分发的后台内核**,不是一个应用。后端以 NuGet 包交付,消费者在 `Program.cs` 里调用 `AddSmartAdmin` / `MapSmartAdmin` 就拿到认证、RBAC、多机构数据权限、字典、配置、日志、上传;前端以 npm 包 `smart-admin-web` 交付,与 NuGet 同一个版本号。

- `backend/` —— .NET 10 内核(产品本体)+ 样例宿主 + 测试。
- `web/` —— Vue 3 + Naive UI 前端内核,npm workspace:`packages/admin` 是包 `smart-admin-web`(布局、路由、stores、共享组件、全部内置页),`template` 是消费者 `npx degit SmartCode-X/SmartAdmin/web/template web` 拿走的薄壳应用,升级只改 `smart-admin-web` 的版本号。

统领一切的约束是**可替换性**:每个服务都有接口、实现类 `public`、方法 `virtual`、注册一律 `TryAdd`,消费者不 fork 就能换掉任意一环。

## 写代码前先知道的六条

1. **注册用 `TryAdd*`,不用 `Add*`。** 可替换服务用了 `Add*`,消费者就顶不掉它;`ReplaceabilityTests`(按 `ReplaceabilityContract` 的扩展点清单逐条校验)会当场判红。
2. **权限码就是规范化路由**(`GET:/api/v1/ping`)。代码里**不存在**权限字符串常量,授权是在角色-菜单界面上勾路由。别发明 `"sys:user:add"` 这类串。
3. **错误只返数字码,不返文案。** 抛 `AdminException(ErrorCode.X)`;文案由前端按 `msgKey` 翻译。分段见 `backend/src/SmartAdmin.Core/ErrorCode.cs`,消费者自己的码从 60000 起。
4. **审计字段由 AOP 自动填**(`Id`、`CreateTime`、`CreateUserId`、`CreateOrgId`、`UpdateTime`、`UpdateUserId`)。业务代码只写业务字段。`CreateOrgId` 是数据范围的锚点,手工绕开它会让机构范围查询返回 0 行。
5. **包依赖只能向下**:Core → SqlSugar → Services → AspNetCore。核心四包的运行时依赖只允许 SqlSugarCore + `Microsoft.*`,引第三方就得下沉到可选包。实体住在 `SmartAdmin.Services`,实体基类住在 `SmartAdmin.SqlSugar`,**不在 Core**。
6. **依赖版本集中在 `backend/Directory.Packages.props`**,不要往各个 `.csproj` 里写 `Version`。

## 前端

- 包的公开 API 就是 `web/packages/admin/src/index.ts`:应用只能 `import { X } from 'smart-admin-web'`,改导出签名即破坏性变更。包内别名是 `#/` → `src/`,**包里不写 `@/`**(到了应用里它指应用自己的 `src`)。
- 持有全局单例的依赖(vue、vue-router、pinia、vue-i18n、naive-ui、@vueuse/core、@iconify/vue、smart-naive-table、smart-naive-icon)是 peerDependencies,由应用装、只有一份。
- 应用经 `createSmartAdmin({ views, locales, routes, menuTitles, icons, iconSets, install, plugins, ... })` 扩展内核,覆盖顺序 内核 < 插件 < 应用;页面 key 是 `views/` 之后去掉 `.vue` 的路径,即菜单的 `component` 字段。
- `web/packages/admin/src/api/schema.d.ts` 是从跑着的后端 `/openapi/v1.json` 生成的,**不要手改**,跑 `npm run gen:api`。
- 写页面前先读 `web/COMPONENTS.md`(SmartTable、FormContainer、useConfirm、字典套件、图标)。加了共享组件要回头更新它,并从 `index.ts` 导出。
- 菜单树是登录后从后端拉的动态路由,不写在 `router/routes.ts` 里。

## 提交与文档

- 提交信息:**中文主题 + conventional-commit**,`type(scope): 主题`,`type`/`scope` 保持英文小写,结尾不加句号。词表见 `skills/write-commit.md`。
- 改 `site/` 下任何一页之前先读 `skills/write-docs.md`:中文是母版、英文是译文,标点与开头有硬规矩,`cd site && npm run lint:prose` 会拦。
- 建模块、建实体、写 CRUD、替换内置服务,`skills/` 下都有对应的流程文件,照着走比自由发挥快。

## 别做的事

- 别改 `CHANGELOG.md` 之外的版本号来「顺手对齐」;版本由发版流程统一改(`skills/smart-release.md`)。
- 别在 `SmartAdminSetup` 里动 `ApplicationAssemblies` 那条路径:断了它,消费者的表不会建、控制器会 404,而且不报错。
- 别把生产建表闸门、JWT 密钥校验这类 fail-fast 改成静默兜底。它们的价值就在于大声失败。
