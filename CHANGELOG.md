# 更新日志

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

版本号规则：**主版本号 = 内核所用的 .NET 主版本**（10.x 对应 .NET 10，下一次大版本跟随下一个 .NET LTS），次版本号加功能，修订号修 bug；全部 NuGet 包、前端包 `smart-admin-web` 与 `SmartAdmin.Templates` 共用一个版本号。破坏性变更尽量攒到换 .NET 大版本时一起发；周期内确需破坏的在次版本发，并在该版本段落顶部加粗提示，升级前先读 *Changed*。

发布节奏：**开发在 `dev` 上进行，发布在 `main` 上完成**。先把 `dev` 合进 `main`，再**在 `main` 上**打 `v*` tag。tag 一推，`release` workflow 校验 tag 落在 `main` 上，跑构建、测试和模板冒烟（`dotnet new smart-app` 必须能还原并编译通过），全绿才打包，经 Trusted Publishing 推 nuget.org、发 npm，同时建 GitHub Release。

逐步的发版操作清单（改版本号、验证、合 `main`、打 tag）见 [`docs/releasing.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/docs/releasing.md)。

> 发版时**前后端版本号必须一起改**：后端版本由 tag 经 `-p:Version` 注入；前端版本写在 `web/package.json`、`web/packages/admin/package.json`（发到 npm 的包）与 `web/template/package.json`（**显示在模板登录页页脚**），模板对 `smart-admin-web` 的依赖钉同一个号，文档站导航的两处版本徽章也跟着改。漏改一处，`release` 的 verify 就会拦下，否则 npm 上的包、界面上的版本会和实际安装的 NuGet 包对不上。

> **发了什么，以本文件为准**，而不是仓库的发行版页面：那里只是可选的镜像，内容从本文件对应版本的段落复制过去。

## Unreleased

### Added

- **应用可以生成自己的 `ph` 离线图标子集。** `smart-admin-web` 带上命令 `smart-admin-icons`：扫描应用 `src` 里的 `ph:*` 名字，从 Phosphor 整集裁出这些图标写成 JSON；`--check` 只比对不写盘，产物过期或有拼错的名字时非 0 退出，给 CI 用。`createSmartAdmin` 新增 `iconSets` 选项，启动时把这份子集和内核子集一起同步注册，业务页的 `ph` 图标首帧就能离线渲染，不再懒加载整套 `ph`（约 946 KB gz）。模板已经接好：`npm run gen:icons` 写出 `src/assets/icons/ph-subset.json`，`main.ts` 经 `iconSets` 传入。已有应用照这两处补上即可。应用的子集不剔除内核子集已有的名字，内核升级不会让应用提交的子集过期。（[#3](https://github.com/SmartCode-X/SmartAdmin/issues/3)）

### Fixed

- **模板装依赖不再提示 esbuild 的安装脚本待批准。** `web/template/package.json` 加上 `"allowScripts": { "esbuild": true }`。degit 出去的模板没有 lockfile，esbuild 的补丁版本会浮动，所以按包名批准、不钉版本；npm 11 对未批准的依赖安装脚本会在 `npm install` 末尾列出警告。
- **网关子路径部署下，外部登录回调与待绑定认领不再一律 40014。** 两个 binder cookie（`tn_oauth_state`、`tn_oauth_pending`）的 Path 跟随对外路径前缀：配了 `CallbackBaseUrl` 取它的路径部分（`https://gw.example.com/admin` → `/admin/api/v1/auth/external`），没配时（仅开发环境）取 `Request.PathBase`，开发环境回退拼出的回调地址也带上 PathBase。根路径部署不受影响。在网关上改写 cookie Path 的临时绕法（如 nginx `proxy_cookie_path /api/ /admin/api/;`）升级后不再匹配，自然失效，可以删掉。（[#1](https://github.com/SmartCode-X/SmartAdmin/issues/1)）
- **`smart-admin-web` 的枚举可以当值导入。** 包入口把 `types/api` 整体导出（类型与枚举一起），`DuplicateStrategy`、`DataScopeType`、`NoticeType`、`ReceiverType` 与 `Job*` 系列都能直接用成员值，比如给 `ImportWizard` 传 `:strategies="[DuplicateStrategy.Skip]"`，不必再自己镜像一份数值。（[#2](https://github.com/SmartCode-X/SmartAdmin/issues/2)）

## 10.10.0 - 2026-09-13

后端 13 个 NuGet 包、前端 npm 包 `smart-admin-web` 与项目模板同号发布。能力清单见 [`README.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/README.md) 的「内置功能」，接入与扩展见[文档站](https://smartcode-x.github.io/SmartAdmin/zh/)。

### Added

- **后端内核。** 元包 `SmartAdmin` 引入四个分层包：`SmartAdmin.Core`（契约）、`SmartAdmin.SqlSugar`（数据层）、`SmartAdmin.Services`（领域服务）、`SmartAdmin.AspNetCore`（宿主集成），宿主里 `AddSmartAdmin` / `MapSmartAdmin` 两行接入。覆盖认证与会话、RBAC（权限码即规范化路由）、五种数据范围、多应用门户、组织与用户、字典与配置中心、通知公告、日志、文件、定时任务，以及 SQLite / MySQL / SQL Server / PostgreSQL 四种方言与多副本部署。内置服务一律接口化、`virtual`、经 `TryAdd` 注册，消费方可以整体替换，也可以子类覆写单步。
- **可选包与工具包。** `SmartAdmin.Excel`（xlsx 导入导出）、`SmartAdmin.Caching.Redis`（Redis 缓存，多副本共享会话）、`SmartAdmin.Auth.WeCom` / `.DingTalk` / `.GitHub` / `.WeChat`（第三方登录）；测试基础设施 `SmartAdmin.Testing`；项目模板 `SmartAdmin.Templates`（`dotnet new smart-app`）。
- **前端内核 `smart-admin-web`。** 布局壳、动态菜单路由、登录鉴权、`v-auth`、全部内置页、共享组件、stores 与语言包，预编译成 ESM + `.d.ts` + 一份 `style.css`。应用从 `web/template` 起步（`npx degit SmartCode-X/SmartAdmin/web/template web`），自己的页面、文案、静态路由、图标经 `createSmartAdmin(...)` 交给内核，页面 key 与内置页相同即覆盖内置页；vue、vue-router、pinia、vue-i18n、naive-ui、@vueuse/core、@iconify/vue、`smart-naive-table`、`smart-naive-icon` 是 peerDependencies，由应用安装。
