# 升级到新版本

后端的 NuGet 包和前端的 `smart-admin-web` 共用一个版本号。升级就是把两边改成同一个新号、装上，再对着更新日志过一遍破坏性变更。你自己的页面、文案和 API 都在自己的仓库里，升级碰不到它们。

## 两边改成同一个号

后端改项目文件里全部 `SmartAdmin*` 包的 `Version`，用了中央包管理就改 `Directory.Packages.props`。`dotnet new smart-app` 生成的工程里有两处：主工程的 `SmartAdmin`，和 `Tests/` 里的 `SmartAdmin.Testing`。

前端在 `web/` 下装同一个号（`X.Y.Z` 换成后端包的版本号），这条命令会同时改写 `package.json` 和 `package-lock.json`：

```bash
npm install --save-exact smart-admin-web@X.Y.Z
```

两边的号要对齐，因为前端包里的内置页面是照同一版后端的接口写的。前端新、后端旧，页面就会去调一个后端还没有的端点。所以 `package.json` 里要写精确版本，模板就是这么写的。`--save-exact` 不带 `^`，重新安装时不会自己跑到更新的次版本。

## peer 依赖由你的应用来装

内核和你的页面要共用同一个应用实例、同一套路由与 store、同一份语言包和图标注册表。所以下面这些依赖在整个应用里只能各有一份，`smart-admin-web` 把它们声明成 peerDependencies，由你的 `package.json` 来装，模板里已经列好：

- `vue`、`vue-router`、`pinia`、`vue-i18n`
- `naive-ui`、`@vueuse/core`、`@iconify/vue`
- `smart-naive-table`、`smart-naive-icon`

装成两份时，内核和你的页面各用各的实例，比如你页面里的 SmartTable 就拿不到内核注入的全局默认值。其余依赖（`echarts`、`md-editor-v3`、`@microsoft/signalr`、`openapi-fetch` 等）随包自动装，不用写进自己的 `package.json`。

新版本抬高了 peer 的版本下限时，`npm install` 会直接报 `ERESOLVE`。按报错把对应的包一起升上去。`--force`、`--legacy-peer-deps` 能把报错压下去，可包只在它声明的范围里测过。

## 装完之后

1. 读[更新日志](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)里这个版本的段落。含破坏性变更的版本，段落顶部有一句加粗提示。
2. 把后端跑起来，在 `web/` 下跑 `npm run gen:api`，重新生成你自己的 `src/api/schema.d.ts`。契约变了的地方，下一步的类型检查会指出来。
3. 跑 `npm run typecheck`，再在浏览器里把自己的页面点一遍。
4. 复制到自己 `views/` 里、用同名 key 覆盖内置页的那几页，升级不会替你更新。对照新版内置页的源码（仓库里 `web/packages/admin/src/views/` 下的同名文件）自己改。

## 登录页的版本号

登录页页脚显示的是 `main.ts` 传给 `createSmartAdmin` 的 `version`。模板传的是 `__APP_VERSION__`，也就是你自己 `web/package.json` 里的 `version`，由 `vite.config.ts` 在构建期注入。它跟着你的应用发版走，升级内核时不用动它。

## 跟踪版本

- [更新日志](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)：Keep a Changelog 格式，每个版本一段，前后端都在里面。破坏性变更尽量攒到换 .NET 大版本时一起发。
- 包只从 `main` 上的 `v*` tag 发布：NuGet 包到 nuget.org，`smart-admin-web` 到 npmjs.com，号码相同。`dev` 分支上的改动在发版之前不会出现在任何一个包里。

反过来，想把自己的修复贡献回 SmartAdmin，看[贡献指南](/zh/community/contributing)。
