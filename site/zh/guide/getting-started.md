# 快速开始

你大概已经开了另一个窗口，准备先把数据库装上。不用装：克隆完仓库直接 `dotnet run`，控制台就打出一串超管密码，浏览器能登进去。SQLite 文件、表结构、种子数据全是内核首启自己长出来的，配置一行没写。

::: tip 前提条件
- .NET 10 SDK
- 想连前端一起跑，再装 Node.js 22.12+
:::

## 先把示例跑起来

仓库自带一个最小化示例宿主 `backend/samples/MinimalHost`，它的 `Program.cs` 只有几行接线。克隆仓库后直接运行：

```bash
dotnet run --project backend/samples/MinimalHost
```

首次启动会自动做三件事。先用默认 SQLite 建表，走 CodeFirst（按实体类自动建表，不用手写建表 SQL），库文件落在 `backend/samples/MinimalHost/data/` 下。再写入种子数据，也就是菜单、角色和超级管理员账号。最后监听 `http://localhost:5100`。这个端口在 `launchSettings.json` 里写死，为的是避开 macOS 上 AirPlay 占用的 5000。

本地想一键起前后端，仓库根的 `dev-start.bat` 会分两个窗口拉起 MinimalHost 和前端 Vite，起 Vite 之前先在 `web/` 下跑一遍 `npm install`。停的时候用 `dev-stop.bat`。

## 确认三个探针

服务起来后，先确认这三个端点都通：

```bash
# 存活探针,只看进程在不在,不碰任何依赖
curl http://localhost:5100/health

# 就绪探针:数据库 + 缓存都连通才返回 Healthy
curl http://localhost:5100/health/ready

# OpenAPI 契约,仅 Development 环境挂载,是前端 gen:api 的数据源
curl http://localhost:5100/openapi/v1.json
```

前两个应该返回 `Healthy`。`/openapi/v1.json` 返回一大坨 JSON，后面生成前端类型要用到它。这个端点生产环境不挂载，线上请求它拿到 404 是预期行为，不是漏配。

## 登录，调第一个接口

`GET /api/v1/ping` 是内核里最小的受保护接口，带有效令牌才放行。登录之前，先弄清密码从哪来。

种子只在 `sys_user` 表为空时跑一次。跑 MinimalHost 是零配置启动，没有显式配密码。内核就自己生成一个 16 位随机密码。随机源是加密安全的，`0/O`、`1/l/I` 这类易混淆字符已经剔掉。它只在**建号那一次**启动的控制台日志里打印，用一个边框圈出来，仅此一次：

```text
════════════════════════════════════════════
  SmartAdmin 首次启动,已创建超级管理员
  账号: superAdmin
  密码: xxxxxxxxxxxxxxxx
  此密码仅本次显示,请登录后立即修改!
════════════════════════════════════════════
```

账号固定是 `superAdmin`，把这串密码抄下来。

::: warning 随机密码只打印一次
没记下来也别慌。本地实验环境里，删掉 `backend/samples/MinimalHost/data` 下的数据库文件，重新 `dotnet run`，空库会重新播种。生产库不能这么清。那边要么先配好固定密码（见下），要么登录后立刻改密。
:::

有时候你想要一个自己说了算的固定密码，比如团队共享、CI、反复删库重来这些场景。做法是把 `backend/samples/MinimalHost/appsettings.Development.json.example` 拷成 `appsettings.Development.json`，再填上 `Seed:AdminPassword`：

```json
{ "SmartAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "你的密码" } } }
```

这个文件装的是本地凭证，被 `.gitignore` 排除，不会进版本库。配了它，启动日志就不再打印随机密码，你直接用自己设的账号密码登。还要注意，种子只认空库。库里只要已经有任意用户，改这里也不会覆盖已存在的账号。真要重置，只能删库重来。

默认没开图形验证码（`Security:Captcha:Enabled` 默认关），登录只要账号密码：

```bash
curl -X POST http://localhost:5100/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"account":"superAdmin","password":"<上面拿到的密码>"}'
```

响应信封里的 `data.accessToken` 就是令牌：

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

超管种子不强制首次登录改密，所以 `mustChangePassword` 是 `false`。只有管理员建号、或者被重置过密码的普通用户，这个值才会是 `true`，前端据此强制跳到改密页。带上令牌调 ping 接口：

```bash
curl http://localhost:5100/api/v1/ping \
  -H "Authorization: Bearer <accessToken>"
```

返回：

```json
{ "code": 0, "data": { "pong": true, "account": "superAdmin", "at": "2026-07-...T..." } }
```

不带令牌，或者令牌过期、被吊销，拿到的都是 `401`，用的是标准信封，`code=40006`。超管带着令牌里的 `sadm` 声明，自动绕过后续的 `[RolePermission]` 权限码校验。普通用户就没这待遇，得先在菜单管理里挂上对应路由，再到角色管理里授权，才调得通同一个接口，挂路由、授权这一整套流程在[新建业务模块](/zh/guide/business-module)里有完整示范。

## 接进你自己的项目

上面跑的是仓库自带示例。真要把内核接进你自己的 ASP.NET Core 项目，先装元包：

```bash
dotnet add package SmartAdmin
```

然后在 `Program.cs` 里调用 `AddSmartAdmin` 和 `MapSmartAdmin`：

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartAdmin(builder.Configuration);
var app = builder.Build();
app.MapSmartAdmin();

app.Run();
```

`AddSmartAdmin` 负责绑配置，把 JWT、RBAC、数据权限、日志这些服务全注册上。`MapSmartAdmin` 负责挂路由、健康检查，还有 OpenAPI 文档，后者只在 dev 下挂。默认走 SQLite，零配置就能跑。

想跨副本共享会话和缓存，也就是多实例部署，得额外装 `SmartAdmin.Caching.Redis`，并且在 `AddSmartAdmin` **之前**调 `AddSmartAdminRedisCache(builder.Configuration)`。为什么要抢在前面？内核的可替换服务都用 `TryAdd` 注册，谁先注册谁赢。晚于 `AddSmartAdmin`，就抢不过内置的进程内缓存了。没配 `Cache:Provider=Redis` 的时候这行是空操作，单实例开发不受影响。

想要更细粒度的依赖控制，可以只引某一层，比如 `.AspNetCore`、`.Services`、`.SqlSugar`、`.Core`。这些包为什么这么分层、「可替换」到底怎么替，归[核心概念](/zh/guide/concepts)讲透，这里先把它跑起来就够。

> 破坏性变更会在[更新日志](https://github.com/SmartCode-X/SmartAdmin/blob/main/CHANGELOG.md)里明确标出，并尽量攒到下一个大版本发布。开发在 `dev` 分支进行。

## 顺手起前端

仓库里的 `web/` 装着前端包 `smart-admin-web` 的源码和一层薄壳模板，Vue 3 加 Naive UI。在里面起 dev，跑的就是模板，端口 `5173`：

```bash
cd web
npm install
npm run dev
```

内置的反向代理会把 `/api`、`/openapi`、`/hub` 原样转发到后端 `:5100`，想换转发目标用环境变量 `SMART_API_TARGET` 覆盖。这么一来浏览器眼里只有一个源，本地开发不用配跨域。打开对应端口，用同一个超管账号密码登录，就能看到完整后台。

前端重新生成 API 类型用 `npm run gen:api`，这条命令要求后端正在跑，因为它是从运行中的 `/openapi/v1.json` 抓契约，不是离线生成。

::: tip 拿它当自己项目的前端
上面是在仓库里直接跑。你自己的项目只需要那层薄壳模板，内核作为 npm 包装进来：

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

升级内核只改 `smart-admin-web` 的版本号，步骤在[升级到新版本](/zh/guide/upgrade)。模板里有哪些文件、页面和文案怎么接进内核，归[前端模板](/zh/guide/frontend-templates)讲。
:::

## 换掉默认数据库

零配置默认用 SQLite，连接串是 `Data Source=./data/SmartAdmin.db`，相对 ContentRoot 解析。换正式数据库不用改代码，一段 `SmartAdmin:Database` 配置说了算，改 `DbType` 和 `ConnectionString` 两项就行。`Sqlite`、`MySql`、`SqlServer`、`PostgreSQL` 都支持：

```json
{
  "SmartAdmin": {
    "Database": {
      "DbType": "MySql",
      "ConnectionString": "Server=127.0.0.1;Port=3306;Database=smart;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;"
    }
  }
}
```

容器化部署走环境变量更顺手（双下划线分层）：

```bash
SmartAdmin__Database__DbType='MySql'
SmartAdmin__Database__ConnectionString='Server=db;Port=3306;Database=smart;User ID=...;Password=...'
```

「换方言」仍是**一条**连接。若还要同进程挂日志库、遗留库，见[配置多数据库](/zh/guide/multi-database)。

::: warning 生产不会自动建表
`ASPNETCORE_ENVIRONMENT=Production` 时，即便开了 CodeFirst 也**不会**自动建表。这道安全闸门防的是线上误改表结构。空库首次上生产，要么临时打开 `EnableCodeFirstInProduction: true` 让它自己建一次，要么让 DBA 手工建。详见[部署指南](/zh/guide/deployment/)。
:::

内核跑通、库也换好之后，下一站是在它上面端到端加一个自己的业务模块，见[新建业务模块](/zh/guide/business-module)。
