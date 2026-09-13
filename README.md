<!-- 顶部居中排版只用顶格独占一行的 <div>、</div>、<img>：本文件也是 NuGet 包说明页，nuget.org 不渲染 HTML，
     打包时这三种行会被删掉（backend/PackageReadme.targets），换成别的标签会在包页上原样露出来。 -->

[English](https://github.com/SmartCode-X/SmartAdmin/blob/main/README.en.md) | 简体中文

<div align="center">

<img src="https://raw.githubusercontent.com/SmartCode-X/SmartAdmin/main/site/public/icon-512.png" width="100" alt="SmartAdmin">

# SmartAdmin

*可替换的现代企业后台管理内核：AI 辅助开发，简单高效；装上即用，升级只改版本号。*

[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE)
[![Stars](https://img.shields.io/github/stars/SmartCode-X/SmartAdmin?style=social)](https://github.com/SmartCode-X/SmartAdmin/stargazers)
[![Forks](https://img.shields.io/github/forks/SmartCode-X/SmartAdmin?style=social)](https://github.com/SmartCode-X/SmartAdmin/forks)
[![NuGet](https://img.shields.io/nuget/v/SmartAdmin)](https://www.nuget.org/packages/SmartAdmin)
[![npm](https://img.shields.io/npm/v/smart-admin-web)](https://www.npmjs.com/package/smart-admin-web)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/github/actions/workflow/status/SmartCode-X/SmartAdmin/ci.yml?branch=main&event=push&label=build)](https://github.com/SmartCode-X/SmartAdmin/actions/workflows/ci.yml?query=branch%3Amain+event%3Apush)

**[📖 文档](https://smartcode-x.github.io/SmartAdmin/zh/) · [🚀 快速开始](https://smartcode-x.github.io/SmartAdmin/zh/guide/getting-started) · [📋 更新日志](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)**

</div>

---

## 这是什么

SmartAdmin 是给 .NET 10 / ASP.NET Core 项目用的企业后台底座。登录、用户、角色、菜单权限、机构与数据权限、字典、配置、日志、文件、定时任务，这些每个后台都要做的功能已经做好，连同管理界面一起发成两个包：

- 后端：NuGet 包 `SmartAdmin`
- 前端：npm 包 `smart-admin-web`（Vue 3 + Naive UI）

装上它们，你就有了一个能登录、能管用户和权限的完整后台，接下来只写自己的业务。内核代码不进你的仓库，升级时把两个包改到同一个新版本号就行；内置的哪一环不合用，都能换成你自己的实现。

## 快速开始

需要 .NET 10 SDK 和 Node.js 22.12 以上。

**第一步：生成后端项目并启动**

```bash
dotnet new install SmartAdmin.Templates
dotnet new smart-app -n MyApp
cd MyApp
dotnet run
```

后端监听 `http://localhost:5100`，默认用 SQLite，首次启动自动建表。控制台会打印超级管理员账号 `superAdmin` 和一个随机密码，密码只显示这一次，先记下来。

**第二步：拉取前端并启动**

另开一个终端，仍在 `MyApp` 目录下：

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

浏览器打开 `http://localhost:5173`，用上一步的账号密码登录。

**已有 ASP.NET Core 项目**：装元包，再在 `Program.cs` 里注册。

```bash
dotnet add package SmartAdmin
```

```csharp
builder.Services.AddSmartAdmin(builder.Configuration,
    o => o.ApplicationAssemblies.Add(typeof(Program).Assembly)); // 本程序集的实体自动建表、控制器自动挂路由
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

前端照第二步拉取。上生产前必须配置 JWT 签名密钥 `SmartAdmin:Jwt:SecretKey`，其余见[部署](https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/)。

## 下一步

| 想做的事 | 看这篇 |
|---|---|
| 加一个业务模块（表、接口、权限） | [加一个业务模块](https://smartcode-x.github.io/SmartAdmin/zh/guide/business-module) |
| 给它做管理页面 | [前端加一个页面](https://smartcode-x.github.io/SmartAdmin/zh/guide/frontend-page) |
| 换成 MySQL / SQL Server / PostgreSQL | [换掉默认数据库](https://smartcode-x.github.io/SmartAdmin/zh/guide/getting-started#换掉默认数据库) |
| 改掉内置行为（登录流程、密码哈希、缓存等） | [替换内置服务](https://smartcode-x.github.io/SmartAdmin/zh/guide/replace-service) |
| 加定时任务、接导入导出 | [定时任务](https://smartcode-x.github.io/SmartAdmin/zh/guide/scheduled-jobs) · [导入导出](https://smartcode-x.github.io/SmartAdmin/zh/guide/import-export) |
| 部署上线、升级版本 | [部署](https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/) · [升级到新版本](https://smartcode-x.github.io/SmartAdmin/zh/guide/upgrade) |
| 了解整体设计 | [核心概念](https://smartcode-x.github.io/SmartAdmin/zh/guide/concepts) · [运行时架构图](https://github.com/SmartCode-X/SmartAdmin/blob/main/docs/architecture/smart-runtime.zh-CN.svg) |
| 让 AI 助手按规范写代码 | [Agent Skills](https://smartcode-x.github.io/SmartAdmin/zh/community/agent-skills) |

## 内置功能

- **认证**：账号密码、JWT 与刷新令牌、登录锁定、在线会话与强制下线、密码策略；可开图形验证码、短信或 TOTP 二次验证、第三方登录（内置 OIDC，企业微信、钉钉、微信、GitHub 为可选包）
- **权限**：角色，目录 / 页面 / 按钮三级菜单，权限码就是接口路由；五种机构数据范围，查询自动隔离，业务代码不写机构条件
- **系统管理**：机构、岗位、用户、多应用门户、字典、配置中心、通知公告、回收站
- **运维**：操作 / 登录 / 异常日志，文件上传（分片续传、签名直链），定时任务（cron、固定间隔、一次性），服务器监控与健康检查
- **数据库**：SQLite、MySQL、SQL Server、PostgreSQL，改一段配置切换；可选 Redis，支持多副本部署
- **管理界面**：动态菜单路由、按钮级权限、明暗主题、三套登录皮肤、中英双语，以及表格、表单、字典、上传、导入向导等常用组件

## 包

| 包 | 说明 |
|---|---|
| `SmartAdmin` | 后端元包，装它就够，包含下面四个内核包 |
| `SmartAdmin.Core` / `.SqlSugar` / `.Services` / `.AspNetCore` | 内核分层：契约、数据层、业务服务、ASP.NET Core 接入 |
| `SmartAdmin.Excel` | 可选：xlsx 导入导出 |
| `SmartAdmin.Caching.Redis` | 可选：Redis 缓存，多副本共享会话 |
| `SmartAdmin.Auth.WeCom` / `.DingTalk` / `.WeChat` / `.GitHub` | 可选：第三方登录 |
| `SmartAdmin.Testing` | 测试项目用：整站起宿主，四种数据库的测试库 |
| `SmartAdmin.Templates` | `dotnet new smart-app` 项目模板 |
| `smart-admin-web`（npm） | 前端管理界面：布局、动态路由、全部内置页面与组件 |

所有包共用一个版本号，前后端填同一个数字；主版本跟随 .NET 主版本（10.x 对应 .NET 10）。

## 参与与许可

欢迎 issue 和 PR。本地开发、跑测试的方法见[贡献指南](https://smartcode-x.github.io/SmartAdmin/zh/community/contributing)，安全问题按 [SECURITY.md](https://github.com/SmartCode-X/SmartAdmin/blob/main/SECURITY.md) 报告。

[Apache License 2.0](https://github.com/SmartCode-X/SmartAdmin/blob/main/LICENSE)
