# SmartAdmin 设计方案 —— 开源企业级小型管理系统内核

> **本文件是设计单源**:文档里的 `§n` 引用都指向本文的节号,改节号要同步;代码注释不引节号,直接写理由。
> **代码与本文冲突时以代码为准**,并顺手把本文改对。过期的权威文档比没有权威文档更害人:
> 它照样被人当依据引用,只是引用的是三个版本以前的事实。凡本文给出的清单(错误码分段、Options、表清单)
> 都在正文里指明了真正的单源在哪一个代码文件。
> 设计取向:零第三方依赖、模块可拆换。代码托管在 GitHub(`SmartCode-X/SmartAdmin`),NuGet 包前缀 `SmartAdmin.*`。

---

## 0. 目标与原则

**一句话目标**:SmartAdmin 是一个基于 ASP.NET Core + SqlSugar 的轻量企业管理系统内核——安装一个 NuGet 包、
在 `Program.cs` 里调用 `AddSmartAdmin` / `MapSmartAdmin` 即可启动,默认提供登录、RBAC、多机构数据权限和 Vue 管理端;**核心运行时除 SqlSugar 外不绑定第三方框架,
所有关键能力均可替换或覆写**。

> 交付物 = 后端 NuGet 包 + 前端 npm 包 `smart-admin-web`(Vue 3 + Naive UI)与应用薄壳模板 + Docker demo。

**设计原则**(排序即优先级):

1. **零配置可跑,全配置可换** —— 每一项能力都有默认实现和默认配置;每一项也都能被配置、替换或继承覆写。
2. **除 SqlSugar 外不引第三方运行时依赖** —— BCL / ASP.NET Core 内置件优先;确实绕不开的(如 Redis 驱动)隔离到可选包。
3. **面向接口 + 模板方法** —— 所有服务先定义接口;实现类 public、方法 virtual、长流程拆小步,用户重写任意一步而不必复制整个方法。
4. **刻意少分包、少抽象** —— 不为想象中的需求建工厂/建层;一个接口只有一个实现时也保留接口,但仅限"用户可能替换"的位置(这是产品能力,不是过度设计)。
5. **契约单源** —— 前端 API 层由后端 OpenAPI 文档生成,前端不手写接口定义(工具链见 §13.6)。

**决策记录**:

| 决策 | 结论 |
|---|---|
| 项目名 | SmartAdmin;GitHub `SmartCode-X/SmartAdmin`;NuGet 包前缀 `SmartAdmin.*` |
| .NET 版本 | 单 TFM `net10.0`,跟随当前 .NET LTS 滚动升级 |
| 包版本 | **主版本号 = 所用 .NET 主版本**(10.x 对应 .NET 10),次版本加功能、修订号修 bug,全部包同号(见 §17) |
| 前端 | 自研设计系统 + 自建;**只做 Naive UI 一套**(逻辑/视图分离,第二套皮肤留作可选) |
| 对象映射 | **不引映射库**(不用 Mapster/AutoMapper,也没引 Mapperly):DTO↔实体手写赋值,少一个依赖、少一层"字段悄悄没映射到"的坑 |
| 多语言 | 前后端 i18n,前端持有文案单源、后端只给错误码(见 §13) |
| 多租户 | **不做**(不是推迟,是整体不做);实体不带租户维度,保持模型简单 |
| 多应用门户 | **做**:独立 `sys_module` 表 + 菜单 `ModuleId`(仅顶级目录);登录选/切应用、每应用独立菜单树、一次加载一个、每用户默认应用。访问权由菜单授权**反推**(非独立权限轴);权限码保持**模块无关** |
| 实时通知 | 在线用户走缓存令牌;通知默认 HTTP 轮询,SignalR 推送为默认关闭的可选增强(ADR-0003,见 §12) |
| 登录会话 | 可配置,默认多端并存;可切单端(新登录踢旧)或限并发数 |
| 验证码 | 默认 SVG(零绘图依赖,跨平台);图片/滑块走 `ICaptchaProvider` 扩展点 |
| 国密 SM2/3/4 | 不进核心;将来做成可选包 `SmartAdmin.Security.Gm`(核心只用 BCL AES/RSA/SHA) |
| 支持数据库 | 官方支持 SQLite/MySQL/SqlServer/PostgreSQL;CI 至少测 SQLite + MySQL |
| 仓库 | **合一仓**(monorepo:后端各包 + `web/` Vue 前端同仓) |
| 功能范围 | 内核子集见 §4,其余以可选包补齐 |
| License | **纯 Apache-2.0**(标准 OSI 开源) |
| uniapp 移动端 | 不做 |

---

## 1. 仓库结构

| 仓库 | 内容 | 发布物 |
|---|---|---|
| `SmartCode-X/SmartAdmin`(**单仓 monorepo**) | `backend/`(各包源码 + 样例宿主 + 测试)+ `web/`(前端 npm workspace)+ `templates/` + `site/` + `docs/` + `docker-compose.yml` | NuGet 包(nuget.org,含 `dotnet new` 模板包 `SmartAdmin.Templates`)+ npm 包 `smart-admin-web`(npmjs.com)+ 前端应用薄壳(同仓 `web/template`,`degit` 取用) |

选合一仓的理由:一次 clone 跑全栈、`docker compose up` 即起 demo、openapi 契约本地生成不跨仓、前后端同步演进、早期少维护一堆仓。

monorepo 目录(核心 5 个包;可选包各自成目录):

```
SmartAdmin/                        # 单仓
├─ backend/                        # 后端独立一层(前后端边界清晰)
│  ├─ src/                         # 核心四包 + 元包 + 可选包(Excel / Caching.Redis / Auth.*),见 §2.1
│  ├─ samples/
│  │  ├─ MinimalHost/             # 零配置最小宿主的验收样例,也是 Docker 镜像与 e2e 的被测宿主
│  │  └─ WorkerHost/              # 只跑定时任务的独立 Worker(ADR-0004)
│  ├─ tests/
│  │  ├─ SmartAdmin.TestHost/     # 扮演"消费方 App"的被测宿主(自带实体/种子/控制器/任务)
│  │  └─ SmartAdmin.Tests/        # xunit v3 + WebApplicationFactory
│  ├─ Directory.Build.props       # 统一版本号、TFM、包元数据
│  ├─ Directory.Packages.props    # 统一依赖版本(中央包管理)
│  └─ SmartAdmin.slnx             # .NET 10 方案格式(等价旧 .sln)
├─ web/                           # Vue 3 + Naive UI 前端:npm workspace = 内核包 smart-admin-web + 应用薄壳模板;自带 Dockerfile + Caddyfile + nginx.conf
├─ templates/                     # dotnet new 模板包(smart-app)+ 冒烟脚本
├─ site/                          # VitePress 双语文档站(中文是母版)
├─ skills/                        # agent skills 单源,.claude/.agents/.codex 只是薄包装
├─ docs/                          # 设计(本文)、规范、ADR、agent 约定
├─ scripts/                       # 多副本冒烟等
├─ .github/                       # workflows + dependabot + issue/PR 模板
├─ Dockerfile                     # 后端镜像(构建 MinimalHost)
├─ docker-compose.yml             # 后端 + Caddy 托管的前端 + MySQL + Redis,一键起全栈
├─ docker-compose.scale.yml       # 双副本叠加文件(多副本保证的验收环境)
├─ .env.example                   # compose 的强制变量样板(JWT 密钥、库口令、超管初始口令)
├─ .editorconfig / .gitattributes # 缩进与行尾
└─ README.md / CONTRIBUTING.md / SECURITY.md / CHANGELOG.md / CLAUDE.md / CONTEXT.md
```

---

## 2. NuGet 包矩阵

### 2.1 包与依赖关系

```
SmartAdmin(元包,无代码)
 └─→ SmartAdmin.AspNetCore
      └─→ SmartAdmin.Services
           ├─→ SmartAdmin.SqlSugar ──→ SqlSugarCore(唯一重量级第三方)
           └─→ SmartAdmin.Core(零第三方依赖:只有 Microsoft.* 扩展抽象)

可选包(装了才有,不受"核心零依赖"约束):
SmartAdmin.Excel           ──→ MiniExcel + DocumentFormat.OpenXml(xlsx 导入导出)
SmartAdmin.Caching.Redis   ──→ StackExchange.Redis(缓存提供方)
SmartAdmin.Auth.WeCom / .DingTalk / .GitHub / .WeChat ──→ 仅 Core + Microsoft.*(外部登录)
规划中(按需再建,不先建空目录):SmartAdmin.Mqtt、SmartAdmin.Security.Gm、SmartAdmin.Storage.Minio、SmartAdmin.Observability

测试支撑包(非运行时,不进元包 SmartAdmin,不受"核心零依赖"约束):
SmartAdmin.Testing         ──→ Microsoft.AspNetCore.Mvc.Testing + 各方言测试驱动(Sqlite/MySqlConnector/SqlClient/Npgsql);不引用任何内核项目,供内核与消费方/卫星包测试项目共用
```

> **核心四包(Core / SqlSugar / Services / AspNetCore)运行时依赖只允许 `SqlSugarCore` + `Microsoft.*`。**
> 一切其余要么自写、要么拷源、要么下沉到可选包 —— 具体逐库处置见 §2.3。
>
> 对象映射不引库,analyzer 形式的 Mapperly 也不引:手写 `new Entity { … }` 赋值就够了,
> 还少一层"新加的字段忘了映射"的静默故障。核心四包连 analyzer 级的第三方依赖也没有。

### 2.2 各包职责

| 包 | 内容 | 依赖 |
|---|---|---|
| `SmartAdmin.Core` | `Result<T>` 统一返回模型、`ErrorCode` + 业务异常体系(`AdminException`)、全部扩展点接口(§5)、Options(§3.2)、雪花 ID 与 `WorkerIdLease`、Channels 事件总线、分页模型、常用扩展方法。**实体基类不在这里**——它们带 SqlSugar 特性,住在 `SmartAdmin.SqlSugar` | 无第三方(只有 Microsoft.* 扩展抽象) |
| `SmartAdmin.SqlSugar` | `SugarClient` 单例封装、`IRepository<T>` 仓储、**实体基类**(`PrimaryId`/`AuditEntity`/`BaseEntity`/`OrgAuditEntity`/`DataEntity`)、CodeFirst 建表、种子数据机制(`ISeedData`)、多库配置解析(`AdditionalDatabases` 多 ConfigId 开口；读写分离策略仍由消费方自定，见站点「配置多数据库」) | Core + SqlSugarCore |
| `SmartAdmin.Services` | 全部领域服务及其 DTO:认证、RBAC、用户/机构/职位/角色/菜单、字典、系统配置、操作/登录日志、本地上传、在线用户;内置种子数据 | SqlSugar |
| `SmartAdmin.AspNetCore` | 控制器(按模块)、`AddSmartAdmin()`/`MapSmartAdmin()`、JWT 接入、统一返回过滤器、全局异常处理、权限/数据范围过滤器、验证码端点、内置 OpenAPI 文档 | Services + ASP.NET Core 框架引用 |
| `SmartAdmin`(元包) | 仅 PackageReference,一键全装 | AspNetCore |
| `SmartAdmin.Caching.Redis`(可选) | `RedisCacheProvider`,把默认 `MemoryCacheProvider` 换成 Redis | Core + StackExchange.Redis |
| `SmartAdmin.Excel`(可选) | xlsx 导入向导与导出的 codec 实现 | Core + MiniExcel + DocumentFormat.OpenXml |
| `SmartAdmin.Auth.*`(可选) | 企业微信 / 钉钉 / GitHub / 个人微信外部登录 provider | Core + Microsoft.Extensions.Http |
| `SmartAdmin.Testing`(测试支撑,非运行时) | 每测试一库的 `TestDb`(SQLite/MySQL/SqlServer/PostgreSQL,模板库克隆加速)、`AdminAppFactory<TEntryPoint>`(一次性库 + 固定超管密码与 JWT 密钥的 `WebApplicationFactory`)、HTTP 信封小助手;供内核自身与消费方/卫星包测试项目共用,不进元包 `SmartAdmin` | Microsoft.AspNetCore.Mvc.Testing + 各方言测试驱动,不引用任何内核项目 |
| `SmartAdmin.Mqtt`(规划中) | MQTT 接入(通知推送等) | Core + MQTT 客户端库 |
| `SmartAdmin.Scalar`(规划中) | `MapSmartAdminApiDocs()`,开发期 API 调试 UI | AspNetCore |

### 2.3 依赖处置(已定稿)

原则:核心四包只允许 `SqlSugarCore` + `Microsoft.*` 作运行时依赖;其余能力要么自写、要么下沉到可选包。

| 能力 | 处置 |
|---|---|
| ORM | `SqlSugarCore`,唯一重量级第三方 |
| JWT | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| OpenAPI | 内置 `Microsoft.AspNetCore.OpenApi` |
| 对象映射 | 不引映射库,DTO↔实体手写赋值 |
| 雪花 ID | 自写单文件,`IIdGenerator` 可换 |
| 压缩 | BCL `System.IO.Compression` |
| 图形验证码 | 自写 SVG,图片/滑块走 `ICaptchaProvider` |
| 事件总线 | 自写 `System.Threading.Channels` 进程内总线 |
| 定时任务 | 内核自研调度器 + 6 段 cron 解析(ADR-0004) |
| 缓存 | `ICacheProvider` 默认 `MemoryCacheProvider`;Redis 走可选包 `SmartAdmin.Caching.Redis`(StackExchange.Redis) |
| Excel 导入导出 | 可选包 `SmartAdmin.Excel`(MiniExcel + DocumentFormat.OpenXml) |
| 对象存储 | 规划中的可选包 `SmartAdmin.Storage.Minio`,走 `IFileStorage` |
| 国密 | 规划中的可选包 `SmartAdmin.Security.Gm`;核心只用 BCL AES/RSA/SHA |
| IP 地理 / UA 精解 | 登录日志只存原文,精解留给可选包 |
| Windows 服务托管 | 不做 |

---

## 3. 用户侧体验(验收基准)

### 3.1 最小启动

用户项目完整 `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSmartAdmin(builder.Configuration);   // 只装内核自己;业务程序集要显式登记,见下
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
```

需要控制扫描/程序集时用带 options 的重载(见 §5.7):
```csharp
builder.Services.AddSmartAdmin(builder.Configuration, options =>
{
    // 内核不扫描程序集:业务模块必须显式登记,否则实体不建表、控制器 404。
    options.ApplicationAssemblies.Add(typeof(DeviceService).Assembly);
});
```

零配置时的默认行为:

- 数据库:SQLite `./data/SmartAdmin.db`,首启自动 CodeFirst 建表 + 种子(默认超管账号,启动日志打印);
- 缓存:`MemoryCacheProvider`(进程内);
- JWT:自动生成开发密钥并持久化到 `./data/dev-jwt.key`,日志输出醒目警告"生产环境必须配置密钥";
- 上传:本地 `./wwwroot/upload`;
- 全部内置端点挂载,前端模板可直接登录。

### 3.2 配置结构(`appsettings.json` 的 `SmartAdmin` 节)

所有子节可整体省略;给出即覆盖默认值。

```jsonc
{
  "SmartAdmin": {
    "Database": {
      "DbType": "Sqlite",              // Sqlite | MySql | SqlServer | PostgreSQL(四种都在 CI 矩阵里跑)
      "ConnectionString": "Data Source=./data/SmartAdmin.db",
      "EnableCodeFirst": true,          // 首启自动建表(生产默认禁,见下)
      "EnableCodeFirstInProduction": false, // 生产必须显式开启才允许自动改表(§12 建表安全)
      "CodeFirstVersion": null,         // 可选:配了就只在它变化时跑 CodeFirst 扫描(记进 sys_schema_version Id=2),实体改了就改号
      "EnableSeed": true                // 首启自动种子(幂等)
    },
    "Cache": {
      "Provider": "Memory",            // Memory(默认,进程内)| Redis(装 SmartAdmin.Caching.Redis 可选包后可用)
      "RedisConnectionString": null,
      "KeyPrefix": "smart:",           // 缓存键前缀,与 §15 会话键 smart:session:{sid} 一致;逻辑键由 ICacheProvider 统一追加
      "PermissionMinutes": 20          // 用户权限码缓存 TTL 兜底(授权变更走显式失效即时生效);0=永不过期
    },
    "Jwt": {
      "SecretKey": null,               // null => 开发密钥 + 警告
      "Issuer": "SmartAdmin",
      "ExpireMinutes": 120,
      "RefreshExpireMinutes": 10080
    },
    "Security": {
      // 口令策略(最短长度、大小写/数字/符号要求、有效期、历史不可复用)**已实现**,但它不在这一节:
      // 它是运营可改项,存 sys_config、走配置中心「安全策略」页,由 ISecurityPolicyProvider 读,
      // 键名见 SecurityPolicyProvider 的常量。这里只放部署期就定死的东西。
      "Captcha": { "Enabled": false, "Type": "Svg" },  // 默认关(保零配置宿主的 API 直登;Web/生产 opt-in);SVG 零绘图依赖,图片/滑块走 ICaptchaProvider
      "LoginLock": { "MaxFailCount": 5, "LockMinutes": 10 },
      "Session": { "Mode": "Multi", "MaxConcurrent": 0 }, // Multi(默认,多端并存)| Single(新登录踢旧);MaxConcurrent>0 时限制并发端数
      "RateLimit": {                    // 请求限流:按客户端 IP 固定窗口,认证端点更严
        "Enabled": true,
        "WindowSeconds": 60,
        "PermitPerWindow": 300,         // 全局每 IP(宽松,挡洪泛);<=0 不限
        "AuthPermitPerWindow": 20       // /api/v1/auth/* 每 IP(更严,挡在线爆破);<=0 不限
      },
      "DefaultInitialPassword": null    // 新建用户/重置密码未给时的默认口令;null => 密码学随机(安全默认,不落公开常量)
    },
    "Upload": {
      "Provider": "Local",             // Local;OSS 类走 IFileStorage 扩展点
      "RootPath": "./wwwroot/upload",
      "MaxSizeMb": 20,
      "AllowedExtensions": [".jpg", ".png", ".pdf", ".xlsx", ".docx", ".zip"]
    },
    "Api": {
      // RoutePrefix / Version 配置化后置(深耦合权限码与菜单种子);内置路由固定 api/v1
      "DisabledModules": [],           // 例:["Dict","Upload"] 关闭对应模块控制器
      "Cors": {                         // 跨源:默认收紧,无源即不放行;经 IStartupFilter 挂 UseCors
        "AllowedOrigins": [],           // 例:["https://admin.example.com"];空=不放行任何跨源
        "AllowCredentials": true
      }
    },
    "Id": {
      "WorkerId": 0,                    // 雪花机器号(0–63);不配则启动时文件锁在本机自动抢号;跨机器/跨容器须每实例配不同值
      "WorkerIdLockDir": null           // 机器号锁目录;默认机器级路径(Windows ProgramData,其它 /tmp),默认目录写不了才改
    },
    "Realtime": {
      "Enabled": false                 // SignalR 推送,默认关;关着时前端退回轮询(ADR-0003)
    },
    "Seed": {
      "AdminAccount": "superAdmin",
      "AdminPassword": null            // null => 随机生成并打印
    }
  }
}
```

上面的 jsonc 只列了最常改的几项,不是全表。**每个子节对应一个 Options 类,全部 public、全部可 `builder.Services.Configure<T>(...)` 覆写**;
类都在 `backend/src/SmartAdmin.Core/Options/`,**字段级默认值以那里为准**:

| 配置子节 | Options 类 | 嵌套子类 |
|---|---|---|
| `Database` | `AdminDatabaseOptions` | `AdminSqlLogOptions`(`Database:SqlLog`,SQL 控制台日志) |
| `AdditionalDatabases`(数组) | `AdminDatabaseConnectionOptions` | —— |
| `Cache` | `AdminCacheOptions` | —— |
| `Jwt` | `AdminJwtOptions` | —— |
| `Security` | `AdminSecurityOptions` | `AdminTotpOptions`、`AdminSessionOptions`、`AdminLoginLockOptions`、`AdminCaptchaOptions`、`AdminRateLimitOptions`、`AdminSmsOtpOptions`、`AdminDataProtectionOptions` |
| `Upload` | `AdminUploadOptions` | —— |
| `Excel` | `AdminExcelOptions` | —— |
| `Email` | `AdminEmailOptions` | —— |
| `ExternalAuth` | `AdminExternalAuthOptions` | `OidcProviderOptions` |
| `Api` | `AdminApiOptions` | `AdminCorsOptions`、`AdminForwardedHeadersOptions` |
| `Realtime` | `AdminRealtimeOptions` | —— |
| `Jobs` | `AdminJobsOptions` | `AdminJobsHttpOptions`、`AdminJobsSqlOptions` |
| `Id` | `AdminIdOptions` | —— |
| `Logging` | `AdminLoggingOptions` | `AdminFileLogOptions`、`AdminOpLogOptions` |
| `Seed` | `AdminSeedOptions` | —— |
| `DemoMode`(裸 bool) | —— | 只放行 GET/HEAD/OPTIONS,写请求一律 41002 |

顶层的 `ApplicationAssemblies` 与 `ErrorCodeEnums` 不从配置绑定,只在 `AddSmartAdmin(cfg, o => …)` 的委托里给。

雪花机器号有个坑值得单独说:**读生效值要经 DI 拿 `AdminIdOptions`,不要读 `SmartAdminOptions.Id`**。
没显式配 `WorkerId` 时,启动阶段的文件锁会抢一个号写回 `AdminIdOptions`,而那份顶层快照不会跟着变。

---

## 4. 功能范围

**内核内置**(装元包即得):

| 模块 | 说明 |
|---|---|
| 认证 | 账密登录 + 图形验证码、JWT + Refresh Token 轮换、登录锁定、登出、在线会话列表/强退;短信免密登录与短信二次验证;可选 TOTP 二因子与 Cookie + CSRF 会话模式(默认关,ADR-0006) |
| 外部登录 | 内置 OIDC + `IExternalAuthProvider` 扩展点,个人中心绑定/解绑,未绑定默认拒绝(ADR-0002 / ADR-0007) |
| RBAC | 角色、菜单(目录/页面/按钮三级)、权限码即规范化路由、角色-菜单授权、角色转授策略 |
| 模块/应用 | **多应用门户**:模块管理(`sys_module`);菜单按模块分区(顶级目录挂模块);登录后选/切应用,每应用独立菜单树、一次加载一个;每用户默认应用。模块访问权由菜单授权反推,权限码保持模块无关 |
| 数据权限 | **多机构数据范围**(全部/本机构/本机构及以下/仅本人/自定义机构),ORM 全局过滤器强制,业务层零过滤代码;用户、机构、文件管理同样遵守调用者范围 |
| 组织 | 用户、机构(树)、职位;用户多角色、主属机构;回收站(软删恢复与永久删除) |
| 字典 / 配置 | 字典类型 + 字典项(前端下拉数据源);键值配置分组页签,带缓存,变更即失效并广播事件 |
| 通知公告 | 全员/角色/用户定向发布;SignalR 实时推送为默认关闭的可选增强(ADR-0003) |
| 日志 | 操作日志(过滤器自动记录,敏感输入脱敏)+ 登录日志 + 异常日志,查询/清空 |
| 文件 | 本地上传 + 下载 + 列表 + 大小限制 + 后缀白名单 + 路径穿越防护;分片续传与秒传;签名直链;软删文件磁盘回收 |
| 定时任务 | 内核自研调度器:cron / 固定间隔 / 一次性触发,编译类 / HTTP / SQL 三种载荷,DB 选主 + 触发 CAS,可选独立 Worker(ADR-0004) |
| 个人中心 | 改密码、改资料、头像、账号安全(TOTP 绑定) |
| 运维 | 健康检查、限流(集群级计数)、服务器监控、多副本雪花 `WorkerId` 租约 |

**可选包补齐**:xlsx 导入导出(`SmartAdmin.Excel`)、Redis 缓存(`SmartAdmin.Caching.Redis`)、企业微信 / 钉钉 / GitHub / 个人微信登录(`SmartAdmin.Auth.*`)。
**规划中**(有真实需求再做):MQTT(`SmartAdmin.Mqtt`)、国密(`SmartAdmin.Security.Gm`)、OSS/Minio 存储(`SmartAdmin.Storage.*`)、可观测性(`SmartAdmin.Observability`)、代码生成。

### 4.1 非目标(明确不做,防膨胀)

写清楚"不做什么"和"做什么"同样重要:

- 不做多租户;
- 不提供第二套 UI 皮肤;
- 不做代码生成 / 批量修改;
- 不做 Minio / OSS 存储(留作可选包);
- 不做 MQTT 推送(留作可选包);
- 不做国密(留作可选包);
- 不做复杂工作流 / 表单引擎;
- 不承诺生产环境自动改表(§12 建表安全)。

---

## 5. 可重写性设计(核心卖点)

四层覆写能力,按侵入程度递增:

### 5.1 配置覆写
Options 全暴露(§3.2),json 或代码均可。

### 5.2 服务替换
`AddSmartAdmin()` 内部所有注册一律 `TryAdd`:

```csharp
// 框架内部
services.TryAddScoped<IUserService, UserService>();

// 用户侧:在 AddSmartAdmin() 之前注册即生效
builder.Services.AddScoped<IUserService, MyUserService>();
builder.Services.AddSmartAdmin(builder.Configuration);
// 或之后 Replace
builder.Services.Replace(ServiceDescriptor.Scoped<IUserService, MyUserService>());
```

### 5.3 继承覆写(模板方法)
所有服务实现类 `public`、方法 `virtual`;长流程拆成可独立覆写的小步。示例签名草案:

```csharp
public class AuthService : IAuthService
{
    public virtual async Task<LoginOutput> LoginAsync(LoginInput input)
    {
        await ValidateCaptchaAsync(input);
        var user = await ValidateUserAsync(input);      // 账密校验,可换成 LDAP/AD
        await CheckLoginPolicyAsync(user);              // 锁定/停用检查
        var token = await CreateTokenAsync(user);       // 签发逻辑
        await OnLoginSucceededAsync(user, token);       // 写日志、发事件
        return BuildLoginOutput(user, token);
    }
    protected virtual Task ValidateCaptchaAsync(LoginInput input) { ... }
    protected virtual Task<SysUser> ValidateUserAsync(LoginInput input) { ... }
    protected virtual Task CheckLoginPolicyAsync(SysUser user) { ... }
    protected virtual Task<TokenPair> CreateTokenAsync(SysUser user) { ... }
    protected virtual Task OnLoginSucceededAsync(SysUser user, TokenPair token) { ... }
    protected virtual LoginOutput BuildLoginOutput(SysUser user, TokenPair token) { ... }
}
```

用户只覆写想改的一步:

```csharp
public class LdapAuthService : AuthService
{
    protected override Task<SysUser> ValidateUserAsync(LoginInput input)
        => _ldap.AuthenticateAsync(input.Account, input.Password);
}
```

### 5.4 端点覆写
`SmartAdmin:Api:DisabledModules` 按模块摘除内置控制器(ApplicationPart `IApplicationFeatureProvider`
过滤),用户自写同路由控制器接管;也可整体不调 `MapSmartAdmin()` 只挑子方法
(`MapSmartAdminAuth()`/`MapSmartAdminSystem()`... 每个模块一个)。

### 5.5 扩展点接口清单(默认实现全部可换)

| 接口 | 默认实现 | 典型替换场景 |
|---|---|---|
| `IPasswordHasher` | PBKDF2(BCL `Rfc2898DeriveBytes`) | 对接已有用户库的哈希算法 |
| `ITokenProvider` | JWT | 自定义 token/对接网关 |
| `IDataScopeProvider` | 机构树数据范围 | 自定义数据隔离维度(如租户) |
| `ICaptchaProvider` | **SVG 验证码**(纯字符串生成,零绘图依赖,跨平台) | 图片/滑块/行为验证码 |
| `IPermissionProvider` | 从缓存/Redis 取用户权限码列表(路由集) | 对接外部鉴权中心 |
| `IFileStorage` | 本地磁盘 | OSS/Minio/S3 |
| `IIdGenerator` | 自写雪花 | 数据库自增/GUID v7(BCL 内置) |
| `IEventBus` | Channels 进程内 | RabbitMQ/Kafka 分布式 |
| `ICacheProvider` | `MemoryCacheProvider`(`IMemoryCache`) | `RedisCacheProvider`(可选包,StackExchange.Redis) |
| `IOperationLogStore` | 写数据库 | 写 ELK/ClickHouse |
| `ISeedData`(多实现) | 内置种子 | 用户追加自己的种子类 |

> 验证码默认实现定为 **SVG 验证码**(纯文本生成,零绘图依赖);想要图片/滑块的用户走 `ICaptchaProvider` 扩展点。

**上表只列了几个代表,不是全清单。** 真正的单源是 `backend/tests/SmartAdmin.Tests/ReplaceabilityContract.cs`,
登记着约 60 个扩展点,并且是**双向**闸门:清单里的每一项都必须真被 `TryAdd` 注册、且前置注册能顶掉;
内核注册的每一个 `SmartAdmin.*` 接口也必须出现在清单里。所以新增内置服务时忘了写 `TryAdd`、或者忘了登记,测试当场判红。
清单还区分三类:普通替换点、`TryAddEnumerable` 的多实现集合(`ISeedData`/`IAdminJob`/`ICaptchaProvider`/`IExternalAuthProvider`/`IDatabaseReadyHook`,叠加而非替换)、开放泛型(`IRepository<>`)。

### 5.6 实体扩展
业务实体基类有**五个**(都在 `SmartAdmin.SqlSugar/Entities/`),按需要的能力挑:

| 基类 | 给的东西 | 用在 |
|---|---|---|
| `PrimaryId` | 雪花 `Id` | 明细表、关联表,不需要审计 |
| `AuditEntity` | + `CreateTime`/`CreateUserId`/`UpdateTime`/`UpdateUserId` | 要留痕但不软删、不隔离 |
| `BaseEntity` | + `IsDelete`(`ISoftDelete`,全局过滤) | 要回收站 / 软删 |
| `OrgAuditEntity` | `AuditEntity` + `CreateOrgId`(`IOrgScoped`,数据范围过滤) | 要机构隔离但物理删 |
| `DataEntity` | `BaseEntity` + `CreateOrgId` | 既软删又机构隔离(最常用) |

审计字段由 AOP 自动填,业务代码只写业务字段。`CreateOrgId` 是数据范围的锚点——它没填上,机构范围查询就是 0 行。

**没有 `ExtJson` 扩展字段。** 内置实体要加列,做法是消费者自己建一张 1:1 的扩展表;
真要做 `ExtJson` 得连带定义它的查询语义(各方言的 JSON 函数差异很大),不是加一列的事。

### 5.7 完整走查:用户如何基于本系统开发自己的业务模块

目标:用户在**自己的外部项目**里新增一个"设备台账"模块,不改框架源码,即可拥有增删改查 +
审计 + 数据权限 + 种子。全流程六步,均在用户项目内(照抄即可编译 —— 下面出现的每个类型仓库里都真实存在):

```csharp
// 1) 实体:继承 DataEntity 即自动获得 Id/审计/软删除/机构数据范围字段
[SugarTable("biz_device")]
public class Device : DataEntity
{
    [SugarColumn(ColumnDescription = "设备名")]
    public string Name { get; set; }
    public string Sn { get; set; }
}
// 首次启动 CodeFirst 自动建表(默认仅 Dev 环境,见 §12 生产建表安全)

// 2) 出入参:分页入参继承 PageInputBase(自带 Current/Size + SortField/SortOrder)
public record DevicePageInput : PageInputBase { public string? Name { get; init; } }
public record DeviceAddInput { public required string Name { get; init; } public required string Sn { get; init; } }

// 3) 服务接口 + 实现:**就是普通接口 + 普通类** —— 没有标记接口、没有扫描,注册在第 6 步显式写一行
public interface IDeviceService
{
    Task<PagedList<Device>> PageAsync(DevicePageInput input);
    Task AddAsync(DeviceAddInput input);
}
public class DeviceService(IRepository<Device> repo) : IDeviceService   // 泛型仓储直接注入
{
    public virtual Task<PagedList<Device>> PageAsync(DevicePageInput input)
        => repo.AsQueryable()                        // 数据权限过滤器已自动生效
               .WhereIF(!string.IsNullOrEmpty(input.Name), d => d.Name.Contains(input.Name!))
               // 这个重载先按实体列白名单校验 SortField(非法字段忽略、回退默认序),杜绝排序注入
               .ToPagedListAsync(input, q => q.OrderBy(d => d.Id, OrderByType.Desc));

    // 映射手写。内核不引任何映射库 —— 运行时依赖只有 SqlSugarCore + Microsoft.*(§2.3),
    // Id / 审计字段 / CreateOrgId 由 AOP 回填,业务代码只管业务字段。
    public virtual Task AddAsync(DeviceAddInput input)
        => repo.InsertAsync(new Device { Name = input.Name, Sn = input.Sn });
}

// 4) 控制器:标准 [ApiController];挂 [RolePermission] 即纳入路由级权限校验。
//    **权限码 = 规范化路由**(这里是 GET:/api/v1/biz/device/page),所以路由要带 api/v1 前缀 ——
//    路由写错,授权界面上就找不到这个端点,给谁都授不了权。
[ApiController, Route("api/v1/biz/device")]
public class DeviceController(IDeviceService svc) : ControllerBase
{
    [HttpGet("page"), RolePermission]
    public async Task<Result<PagedList<Device>>> Page([FromQuery] DevicePageInput input)
        => Result<PagedList<Device>>.Ok(await svc.PageAsync(input));

    [HttpPost("add"), RolePermission, OperationLog("新增设备")]
    public async Task<Result<bool>> Add(DeviceAddInput input)
    {
        await svc.AddAsync(input);
        return Result<bool>.Ok(true);
    }
}

// 5) 种子(可选):实现泛型 ISeedData<T>,启动自动执行且幂等。
//    非泛型 ISeedData 只是 DI 收集用的空标记 —— 直接实现它能编译,但启动时反推不出实体类型会崩。
//    Id 必须显式给定且落在消费者保留区间 [SmartSeedIds.ConsumerMin, SmartSeedIds.ConsumerMax](见 Core/SmartSeedIds.cs)。
public class DeviceSeedData : ISeedData<Device>
{
    public IEnumerable<Device> HasData() =>
        [new Device { Id = SmartSeedIds.ConsumerMin, Name = "示例设备", Sn = "SN-0001" }];
}

// 6) 用户项目 Program.cs:AddSmartAdmin / MapSmartAdmin 照旧,另加三处显式登记 ——
//    业务程序集(实体建表 + 控制器挂载)、你自己的服务、以及种子。全是显式的:内核不扫描。
builder.Services.AddSmartAdmin(builder.Configuration, o => o.ApplicationAssemblies.Add(typeof(Device).Assembly));
builder.Services.TryAddScoped<IDeviceService, DeviceService>();   // 忘了这行 → 控制器注入不到服务,请求期才炸
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, DeviceSeedData>());   // 忘了这行 → 种子静默不执行
```

**注册模型(双层,消除"显式 vs 扫描"的歧义)**:
- **框架内置服务**:一律在各模块 `AddXxx()` 里**显式 `TryAdd`** 注册,不靠扫描 —— 可预测、可被用户 `Replace`(§5.2)。
- **用户外部模块**:经 `options.ApplicationAssemblies.Add(...)` **显式登记**业务程序集,其实体参与 CodeFirst 建表、控制器 AddApplicationPart 挂载。
  内核不扫描入口程序集及其引用,也没有打开扫描的开关:显式登记是唯一真源,守的是"显式、可预测、无魔法"。

- **种子**:内置与用户种子<b>都</b>要显式 `TryAddEnumerable` 注册 —— 内核<b>不扫描</b>程序集找种子(`ApplicationAssemblies` 只管实体建表与控制器挂载)。忘了注册的后果是种子**静默不执行**,没有任何报错。种子 Id 须落在保留区间(§5.7 代码注释与 `Core/SmartSeedIds.cs`)。

要覆写**内置**服务/实体行为,回到 §5.1–5.4 四层覆写;要新增**自己**的东西,就是上面这套。
两者对称:内置模块本身也是照这套写的 —— 都走显式注册,全程无扫描、无魔法。

---

## 6. 后端横切机制

- **统一返回**:`Result<T> { Code, MsgKey, Args, Message, Data }`,由 `ResultEnvelopeFilter` 把裸返回包上;业务抛 `AdminException(errorCode)`,由 `AdminExceptionFilter` 统一转换。框架级 400(模型校验)与未捕获 500 **刻意不套信封**——程序缺陷该大声失败。错误码与文案见 §13,**不在抛出处写死中文串**。
- **权限过滤(不引入硬编码权限串)**:
  - 标记特性 **`[RolePermission]`**(无参数)标注"此接口需授权";**权限码 = 规范化路由**。约定**含 HTTP Method**(`POST:/api/v1/biz/device/add`),以区分同路径不同操作(`GET`/`POST /biz/device`);不手写 `"sys:user:add"` 这类字符串。授权单元是菜单按钮(一种能力),一颗按钮可挂多条路由(`;` 连接,`PermissionCode.Split/Join`);每页固定 查询 / 新增 / 更新 / 删除 四颗,查询 = 列表 + 详情,删除含批量删除。
  - 授权管道:`[RolePermission]` 里超管(`sadm` claim)直接放行 → 普通用户取 `PermissionCodeList`(**从缓存读**,`IPermissionProvider` 可换)判断是否包含当前路由。同一处顺带校验会话(`sid`)是否仍有效,所以强退立刻生效。不需要具体权限、只要求已登录的端点用 `[ActiveSession]`;匿名端点就不挂这两个特性。高风险写操作另有 `[RequireReauth]`。
    没有 `[SuperAdmin]` / `[IgnoreRolePermission]` 这类特性,也不需要:超管由 `sadm` 放行 + 服务层判断覆盖,"忽略权限"等于不挂特性。
  - **前端权限常量:没做,也不打算做。** `v-auth` 直接写路由串(`v-auth="'POST:/api/v1/user/add'"`)。生成一层常量要多一条 CI 产物和一次跨仓对账,换来的只是少写一个字符串——而权限码本来就是路由,读代码的人不需要再查一张表。
  - **数据范围**:模型为**按角色**配置(`sys_role_data_scope`,每角色一条,五种范围:全部/本机构/本机构及以下/仅本人/自定义),用户生效范围 = 其各角色范围的**并集**(All 最宽优先)。`IDataScopeProvider` 解析并按用户缓存;由 **SqlSugar 全局查询过滤器**对实现 `IOrgScoped` 的实体(`DataEntity` 及子类)自动注入 `CreateOrgId ∈ 范围` / `CreateUserId == 本人` 的 WHERE——业务表继承 `DataEntity` 即自动受控,无需每个接口手写校验。
    - 生效范围经 `IDataScopeContext` 在**授权管道**(动作执行前)按当前用户解析并写入;HTTP 侧用 `HttpContext.Items` 承载(避免授权过滤器内 AsyncLocal 不回流的陷阱),非 HTTP 用 AsyncLocal。
    - 数据范围**按角色**全局过滤(实现更简、覆盖更全、不易漏挂),而不是按接口(role×api)配置;更细的"同角色不同接口不同范围"留作后续扩展。`SqlSugar 全局过滤器只认接口/精确类型、不认基类`,故用标记接口 `IOrgScoped` 匹配(与软删过滤器走 `ISoftDelete` 同理)。
    - **范围边界**:`sys_user` 本身继承 `BaseEntity`(非 `DataEntity`),用户列表**不**走通用机构过滤(用户的机构维度是其 `OrgId` 而非 `CreateOrgId`,属特例,如需按机构筛用户在 `UserService` 显式处理);无角色 / 有角色但未配范围 → 默认"仅本人"(不放大可见面)。会话/强退/在线的模型见 §15。
- **多应用门户(模块分区)**:菜单按 `sys_module` 分区——`ModuleId` 仅顶级目录设置,子节点归属由内存树上溯到根目录解析。"我的模块"由菜单授权**实时反推**(用户被授权某模块下任一菜单即拥有该模块,超管见全部;门户/登录时算,非每请求热路径,不缓存),登录后按选定模块拉菜单树并注册动态路由。**权限码保持模块无关**——切应用只改侧边栏与路由,不改用户持有的 API 权限码(`RbacPermissionProvider` 不按模块过滤;这是回归锁死的不变量)。默认应用为每用户偏好(`sys_user.DefaultModuleId`)。内置 `system` 模块不可删。
- **禁硬编码字符串**(全局约定):缓存键集中在 `Core/CacheKeys.cs`;权限码就是规范化路由,不设字符串常量;面向用户的文案走 i18n 资源 + 错误码枚举;魔法数走枚举。代码评审把"裸字面量字符串"当味道。
- **操作日志**:`[OperationLog]` 特性(标题可取常量/资源键)+ 过滤器自动记录(入参/耗时/结果码),敏感字段脱敏配置。
- **缓存策略**:用户权限/菜单/字典/配置进缓存,变更即失效(事件总线广播),目标是登录后接口 10-30ms。
- **OpenAPI**:内置 `AddOpenApi()`,Development 环境挂 `/openapi/v1.json`,是前端 `npm run gen:api` 的契约源;`release` workflow 把 `openapi.json` 随 GitHub Release 一起发布。

---

## 7. 前端设计(Vue Naive UI)

### 7.1 先行产物:`DESIGN.md` 设计系统规范

前端的视觉由一份规范(`web/DESIGN.md`)和一份 design tokens(CSS variables,`web/packages/admin/src/styles/tokens.css`)决定。

大纲:

```
DESIGN.md
├─ 1. 设计基调:企业级、精简、留白充分、低饱和主色、明暗双主题
├─ 2. Design Tokens(CSS variables 单源)
│   ├─ 色板:主色/中性色阶/语义色(success/warning/danger/info),暗色映射
│   ├─ 字体:字族、字号阶梯(12/13/14/16/20/24)、行高、字重
│   ├─ 间距:4px 基准网格(4/8/12/16/24/32)
│   └─ 圆角(4/6/8)/阴影(3 级)/边框/过渡时长
├─ 3. 布局:侧边导航(可折叠 236↔76)+ 顶栏(面包屑/搜索/用户区)+ 内容区(页签可选)
├─ 4. 核心页面形态(每种一张规范图)
│   ├─ 列表页:筛选区 + 工具栏 + 表格 + 分页
│   ├─ 表单弹窗 / 抽屉:何时用弹窗何时用抽屉
│   ├─ 树+表联动页(机构-用户、菜单管理)
│   └─ 详情页 / 授权面板
├─ 5. 组件规范:按钮层级、表格密度、空态/加载态/错误态、消息反馈
└─ 6. 可访问性:对比度 ≥ 4.5:1、焦点态、键盘导航
```

风格方向:现代企业感(Stripe/Vercel 一类)。

**设计生产流水线**(可用 AI 设计工具,不必手绘每一屏):
1. **选主题**:用 **typeui MCP**(自带多套主题 skills)挑一套接近目标风格的主题作为 token 起点;
   `pencil` MCP、`awesome-design-md` 一类工具可作补充参考(本仓不带)。
2. **出稿**:关键页(登录、列表页、表单弹窗、树+表、授权面板)用 Figma / Claude / Pencil 生成高保真稿,统一走 §7.1 页面形态。
3. **导出 token**:把主题/稿件的色板、字阶、间距导出成 **一份 CSS variables**(design tokens 单源)。
4. **落地**:前端消费这份 tokens;组件库只做"渲染载体",视觉由 tokens 决定。

### 7.2 技术栈

| 项 | 选型 |
|---|---|
| 框架 | Vue 3.5+ / Vite / TS |
| 组件库 | **Naive UI 单套**(CSS vars 深度换肤对齐 tokens) |
| 状态 | Pinia |
| 路由 | vue-router,菜单驱动动态路由 |
| API 层 | `openapi-typescript` 生成类型 + `openapi-fetch` 发请求(不用 axios) |
| 工程 | **oxlint + Prettier**;没有 git hooks(无 husky / commitlint / lint-staged)——提交规范靠 `skills/write-commit.md` 和 PR 评审,不靠钩子 |

**Vue = 只做 Naive UI 一套,但按"逻辑/视图分离"写**:
- 只交付 **Naive UI** 一套 Vue 前端,写法完全地道,不做适配层(适配层会抹平组件库个性,已否)。
- 但从第一天就把**逻辑沉进 composables**(请求、分页/搜索状态、权限判断、字典、表单校验规则、路由/菜单数据都与 UI 无关),`.vue` 视图只做 markup + 装配。
- 这样"支持第二种 UI"退化为**将来补一层视图**、而非重写:第二套皮肤列为 **可选**,有真实需求再做。
- **这条边界没有守住**:`composables/` 里 `useConfirm`、`useLayoutMenu`、`useTheme`、`useRealtime` 都直接 import 了 `naive-ui`,
  也没有 lint 规则拦。所以"补第二套皮肤只写视图"这个承诺不成立,真要做得先把这几个 composable 拆成"与 UI 无关的核心 + 一层 Naive 适配"。
  **没有闸门的约定就只是一句愿望**——写进设计文档不等于它会发生。
> 为什么不双 UI:本项目卖点是后端 NuGet 一行启动;一套打磨到位的 Naive 版对采用度的作用大于两套各半成品,也避免第二套皮肤长期失修(§10 风险)。

**契约单源机制**:后端在 Development 环境挂 `/openapi/v1.json` → 前端一条
`npm run gen:api` 拉取并重新生成 `schema.d.ts`,类型化客户端随之更新;接口定义零手写、前后端零漂移。

**前端通用约定**:`v-auth` 权限指令(按钮级权限码控制显隐)、明暗主题切换、i18n 多语言、
请求层统一错误处理(对齐后端 `Result` 与错误码)、响应式适配(桌面优先,窄屏可用)。

### 7.3 页面范围(对齐后端)

登录、工作台(简)、用户管理(机构树+表)、机构、职位、角色(含菜单授权/数据范围授权)、
菜单管理(顶级目录可选所属应用)、模块/应用管理、字典、系统配置、操作日志、登录日志、在线用户、个人中心。

### 7.4 前端架构

按"逻辑/视图分离"组织目录,`composables` 承载逻辑。
参考对象:soybean-admin、naive-ui-admin。

**分发形态**:前端内核整体发成一个预编译 npm 包 `smart-admin-web`,与 NuGet 包同号;消费方从薄壳模板起步,升级只改版本号。持有全局单例的依赖(vue、vue-router、pinia、vue-i18n、naive-ui、@vueuse/core、@iconify/vue、smart-naive-table、smart-naive-icon)是 peerDependencies,由应用装、只有一份。应用经 `createSmartAdmin({ views, locales, routes, plugins, ... })` 把自己的页面、文案、路由交进来,覆盖顺序 内核 < 插件 < 应用。

**目录结构**:
```
web/
├─ packages/admin/     # 包 smart-admin-web;公开 API 面 = src/index.ts,包内别名 #/ → src/
│  └─ src/
│     ├─ api/            # client.ts(openapi-fetch 封装,createApiClient)+ index.ts(内核端点)+ schema.d.ts(openapi-typescript 生成,别手改)
│     ├─ composables/    # 逻辑复用:useModule/useAuthMenu/useConfirm/useTheme… (注:部分直接依赖 naive-ui,见 §7.2)
│     ├─ components/     # 通用业务组件:FormContainer、字典套件、图标… 清单在 web/COMPONENTS.md(SmartTable 在独立包 smart-naive-table)
│     ├─ layouts/        # 布局骨架 + Header/Menu/Tabs
│     ├─ router/         # 静态路由(登录/错误/壳)+ 页面表 viewRegistry + 菜单驱动的动态路由(只活在内存,刷新时守卫重建)
│     ├─ stores/         # Pinia:user、auth、app、dict、tabs
│     ├─ directives/     # v-auth(按钮级权限码 = 路由)
│     ├─ locales/        # zh-CN.ts / en-US.ts + registerLocales 深合并,带中英键对齐测试(parity.spec.ts)
│     ├─ lib/ utils/ styles/ theme/ types/ assets/
│     ├─ views/          # 内置页面:按域分组
│     ├─ createSmartAdmin.ts   # 初始化函数
│     └─ index.ts            # 公开导出
├─ template/           # 应用薄壳 smart-admin-app:main.ts 一次 createSmartAdmin;src/views、src/locales/ext 放应用自己的东西;e2e 宿主
└─ e2e/                # Playwright,跑在 template + MinimalHost 上
```
包里没有 `hooks/`、`enums/`、`config/`、`typings/` 这类目录,别照别的脚手架补建。

**关键机制**:
- **布局系统**:**默认竖向侧边栏布局**(企业最常用),设置抽屉里可切六种布局模式(竖向、竖向混合、横向及几种顶栏/侧栏混合,全集是 `stores/app.ts` 的 `LAYOUT_MODES`)。左侧菜单 + 顶栏(面包屑/搜索/主题/语言/用户)+ 内容区(可选多页签 Tabs)。布局状态存 `stores/app`,支持折叠 236↔76、明暗主题、主题色。
- **菜单与动态路由**:登录后拉取用户菜单树 → 生成动态路由并注册 → 渲染侧边菜单;刷新页面走"路由守卫重建"避免白屏。菜单的 `component` 字段是页面表的 key(`views/` 之后去掉 `.vue` 的路径),页面表由内核内置页 + 插件 + 应用的页面合成。
- **多应用切换(app-switcher)**:登录后拉 `GET /api/v1/personal/modules`——单应用直接进,有默认且可访问进默认,否则弹「选择应用」;空列表提示未分配应用。顶栏「切换应用」重选。切换即以选定 `moduleId` 拉 `GET /api/v1/personal/menu?moduleId=` 重建动态路由(整棵菜单树按应用替换)。默认应用经 `PUT /api/v1/personal/default-module` 持久化(每用户)。逻辑沉 `composables`(useModule/useAuthMenu),视图不含 Naive 专有类型。
- **权限**:路由级(动态路由本身即权限过滤)+ 按钮级(`v-auth="'POST:/api/v1/biz/device'"`,权限码就是后端规范化路由,呼应 §6 不硬编码)。权限码列表进 `stores/auth`。
- **请求层**:`openapi-fetch` 封装(不是 axios),统一注入令牌、统一按后端 `Result` + 错误码处理(§13 把错误码映射成本地化文案),401 自动刷新/登出(带单飞与重放)。
- **页面范式**:列表页 = `SmartTable`(`smart-naive-table`:列定义同时驱动搜索表单、分页、排序、工具栏、列设置)+ `FormContainer` 表单弹窗/抽屉;树表页用 `SmartTable` 的静态 `:data` 模式 + `utils/tree.ts` 的 `filterTree`。共享组件在 `components/`,逻辑在 `composables/`,页面只做装配。

---

## 8. 工程与开源基建

- **CI**:GitHub Actions,四个工作流(`ci` / `docker-smoke` / `docs` / `release`)。这里只给意图,**实测结论与门控细节以 `CLAUDE.md` 的 CI 一节和 `.github/workflows/` 为准**,那里记着按路径门控、四方言矩阵、SqlServer 子集策略这些踩出来的东西。
  - `ci`:后端 `[sqlite, mysql, sqlserver, postgres]` 四腿矩阵 + 模板冒烟 + 前端(lint / format / vitest / build)+ 前端 e2e(Playwright)+ 依赖漏洞兜底;仓库公开后另有 CodeQL 与 dependency-review。
  - `release`:`v*` tag → 版本对账(CHANGELOG、`web/` 三个 `package.json`、模板对 `smart-admin-web` 的精确依赖、文档站徽章)+ 打包 13 个 nupkg 与前端 tgz + 抓 `openapi.json` → 经 **Trusted Publishing**(OIDC,仓库里不放 API key 或 npm token)推 nuget.org、发 npm,并建带 CHANGELOG 段落的 GitHub Release。
  - 第三方 action 一律钉 commit SHA(`release` 的 publish job 持 `id-token: write`,浮动 tag 被改写即等于直发 nuget.org 与 npm),升级交给 Dependabot。
- **测试**:`SmartAdmin.Tests`(xunit v3 + `WebApplicationFactory` 集成测试),至少覆盖:
  1. 认证全流程(登录/刷新/锁定);
  2. 数据范围(不同角色查同一接口得到不同数据集);
  3. **可替换性契约本身**(产品承诺,必须回归锁死),锁在 `backend/tests/SmartAdmin.Tests/ReplaceabilityTests.cs`(扩展点清单见同目录 `ReplaceabilityContract.cs`):DI 前置注册即胜出、子类覆写单步、禁用模块后由消费者 Controller 接管路由、消费者种子首启幂等,以及扩展点清单与内核实际注册的双向一致性;
  4. 数据库矩阵:CI 跑 **SQLite / MySQL / SqlServer / PostgreSQL 四条腿**。SqlServer 全量套件要 40–60 分钟(建表开销 × 每测试一个库),
     所以 push / PR 只跑方言敏感子集,全量留给定时任务与手动触发——判据与实测数字在 `CLAUDE.md`。
- **提交规范**:Conventional Commits,**主题写中文**,`type`/`scope` 保持英文小写(如 `feat(web): 无权限用户的按钮不再渲染`)。词表与判据在 `skills/write-commit.md`;没有 git hooks 强制,靠评审。
- **文档**:双语文档站 `site/`(VitePress,中文是母版、英文是译文,写作规矩与闸门在 `skills/write-docs.md`);仓内 `docs/` 只放设计、规范、ADR 与 agent 约定,**不发布到站上**,所以站上正文不能把读者指过来。
- **Demo**:`docker compose up` 一键起全栈(后端 + Caddy 托管的前端 + MySQL + Redis)。密钥与口令是**强制变量**,照 `.env.example` 建 `.env` 才能启动——演示密钥当默认值意味着一条命令就能在公网上跑起一个签名密钥人尽皆知的后台。
- **License**:纯 Apache-2.0(见 §17)。

---

## 9. 里程碑

首发前的三个里程碑(后端骨架、Vue 前端、发布准备)均已完成,§18 的最小验收闭环是首发达标的判据。后续路线不在本文维护:待办与需求走 GitHub issues(`docs/agents/issue-tracker.md`),已发布的变化以 `CHANGELOG.md` 为准。

### 9.1 优先级

- **P0(没有它项目不成立)**:`AddSmartAdmin` / `MapSmartAdmin` 即启动 / 默认 SQLite / 默认超管 / 登录 / RBAC / 多机构数据权限 / Vue 登录到菜单闭环 / OpenAPI / 核心测试(可替换性契约 ReplaceabilityTests + 数据范围)/ NuGet 打包。
- **P1(内核应该有)**:字典 / 系统配置 / 操作日志 / 登录日志 / 本地上传 / Docker demo / i18n / 限流 / 健康检查 / 定时任务 / 导入导出。
- **P2(可选包或后续)**:第二套皮肤 / 非竖向布局 / MQTT / Minio / 国密 / 代码生成 / OpenTelemetry / IP 地理 / UA 精解。

---

## 10. 风险与开放问题

| 项 | 说明 | 处理 |
|---|---|---|
| SqlSugar 版本策略 | 唯一第三方核心依赖,其大版本升级可能破坏 CodeFirst 行为 | 锁定次版本,升级走独立 PR + 集成测试 |
| 前端维护成本 | 只有 Vue 一套(单仓) | 无多前端负担;靠契约单源 + DESIGN.md 把成本压在纯 UI 层 |
| monorepo 双工具链 CI | 一仓两套构建(dotnet + npm) | CI 按路径触发(`backend/**` 跑后端、`web/**` 跑前端)+ 分 job;发版同一个 `v*` tag 同号发 NuGet 与 npm |
| Vue 二皮肤(将来) | 风险已兑现:约束没落地,几个共享 composable 直接依赖 naive-ui(§7.2) | 二皮肤真要做时,先把这几处拆成"无 UI 内核 + Naive 适配";在那之前不承诺"只写视图" |
| 验证码零依赖 | SVG 验证码安全强度低于图片扭曲 | 默认够用(配合登录锁定);高要求场景走 `ICaptchaProvider` 扩展点,文档明示 |
| 轮询实时性 | HTTP 轮询有延迟、频繁拉增加负载 | 通知/在线用 ETag + 合理间隔(如 30s)够用;要低延迟推送切 MQTT 可选包(§12) |
| typeui MCP 外部可用性 | 设计流水线依赖外部 MCP,可能未接入/变更 | typeui 仅用于"起点主题",产物是自持的 CSS tokens;缺失时退回 pencil MCP / awesome-design-md / 手写 tokens,不阻塞实现 |

---

## 11. Docker 发布

系统一等公民地支持容器化,用户不装 .NET SDK 也能起。

- **两份 Dockerfile,别搞混**:仓库根 `Dockerfile` 从源码构建 `MinimalHost`(CI 用);
  `templates/content/smart-app/Dockerfile` 从 NuGet 装内核、构建消费者自己的 host,是脚手架带出去的那份。
  两份都是多阶段、非 root(用镜像自带的 `$APP_UID`,不自己 `adduser`)、监听 8080;根那份还拆了 restore 层让依赖层可缓存。
  两份都**不写 `HEALTHCHECK`**:aspnet 运行时镜像里没有 curl / wget,写了只会恒失败;健康检查交给编排层探 `/health` 与 `/health/ready`。
  上传目录与 SQLite 目录在镜像里预建并改属主,用**具名卷**挂载(bind mount 会用宿主属主覆盖,非 root 直接写不了)。
- **`docker-compose.yml`**:后端 + 前端(**Caddy** 托管静态产物并反代 `/api`,以非 root 跑在 8080;nginx 那份留在 `web/nginx.conf` 作备选)+ MySQL + Redis,一条 `docker compose up` 起全栈。
  它同时是**生产首启路径的实测**:环境刻意是 `Production`,所以 JWT 密钥、生产建表开关、上传目录在 wwwroot 之外三道闸门缺一不可。
  `docker-compose.scale.yml` 叠加出双副本,验的是只有两个副本同时在线才暴露的保证(跨副本强退、集群级限流、`WorkerId` 互异)。
- **配置走环境变量**:`appsettings` 的 `SmartAdmin__Database__ConnectionString` 等用双下划线映射,
  compose/生产 K8s 直接注入,镜像不打包任何密钥。
- **镜像发布:还没做。** 目前 CI 只构建镜像跑冒烟,不推任何 registry,也没有多架构构建。
  真要发的话是 tag 时推 GHCR + `linux/amd64,linux/arm64`,但得先想清楚推的是「样例宿主」还是「给消费者继承的基础镜像」——
  前者对消费者没什么用,后者是一份新的长期契约。
- **数据卷**:`./data`(SQLite/JWT 开发密钥)、`./wwwroot/upload`(本地上传)声明为卷,避免容器重建丢数据。

---

## 12. 其他考量(补漏)

以下为发布前需要成型、但不改变主架构的横切项,大多用 .NET 内置件即可,不新增第三方运行时依赖。

| 项 | 方案 | 归属 |
|---|---|---|
| **i18n / 多语言** | 独立设计,见 §13 | Core + AspNetCore + 前端 |
| **健康检查** ✅ | 内置 `HealthChecks`,`/health`(存活)+ `/health/ready`(依赖:DB/缓存),匿名供编排层探针 | AspNetCore,已落 |
| **限流** ✅ | .NET 内置 `RateLimiter`,按客户端 IP 固定窗口,认证端点更严(`Security:RateLimit`);经 `IStartupFilter` 挂 `UseRateLimiter`,命中出 429 信封(40008) | AspNetCore,已落 |
| **软删除 + 审计** ✅ | `BaseEntity` 带 `IsDelete`/`CreateTime`/`CreateUserId`/`UpdateTime`/`UpdateUserId`,SqlSugar 全局过滤器 + `AOP` 自动填充(含 `DataEntity.CreateOrgId` 从令牌 org claim 回填) | Core + SqlSugar,已落 |
| **API 版本化** | 路由前缀内置版本段(`/api/v1/...`);`RoutePrefix`/`Version` 配置化**后置**(深耦合权限码与菜单种子),固定 `api/v1` | AspNetCore |
| **CORS / 上传限制 / 请求体大小** ✅ | CORS 走 `Api:Cors`(命名策略 + `IStartupFilter` 挂载,默认收紧);上传走 `Upload:MaxSizeMb` + 后缀白名单 | AspNetCore,CORS 已落 |
| **在线用户 / 站内通知** | 在线用户 = 令牌信息本就存缓存,直接列举/强退,无需长连接。实时性:默认 **HTTP 轮询**(前端定时拉未读/在线,`If-None-Match`/ETag 省流);需要低延迟就开内置的 **SignalR 推送**(`Realtime:Enabled`,默认关,ADR-0003);MQTT 留给规划中的可选包 `SmartAdmin.Mqtt`。**没有 `Notify` 配置节**,别照它写配置 | AspNetCore(轮询)/ 可选包(MQTT,规划中) |
| **配置热更新** | 服务读配置用 `IOptionsMonitor<T>`,改 json / 环境不必重启 | 全局约定 |
| **生产建表安全 / DB 策略** | Dev 默认允许 CodeFirst 建表+加列;**生产默认禁**,显式 `Database:EnableCodeFirstInProduction` 才开;内置表用 `sys_schema_version` 记版本;**不做破坏性迁移**(不自动删表/删列/改窄字段);发布说明列出每版表结构变更 | SqlSugar |
| **种子幂等** | `ISeedData` 以主键/唯一键判存,重复启动不重复插入 | SqlSugar |
| **可观测性** | 默认只用内置 `ILogger`;OpenTelemetry(logs/metrics/traces)做 **可选包** `SmartAdmin.Observability` | 可选包(规划中) |
| **统一时间/ID** | 时间用 `TimeProvider`(.NET 内置,可测试);ID 用自写雪花(`IIdGenerator`) | Core |

---

## 13. 多语言(i18n)设计

目标:前后端都能多语言,且**语言资源单源、可被用户扩展/覆盖**,不散落硬编码文案(呼应 §6)。

### 13.1 分工:谁负责翻译什么
- **前端负责全部"界面文案"**(菜单、按钮、表单标签、列头、提示语)——这些后端根本不该知道。
- **后端只负责"消息文案"**(校验失败、业务错误、操作结果)——但**后端不返回死中文**,而是返回**错误码 + 参数**,由前端映射成本地化文案。
- 好处:文案**单源在前端**,后端零翻译负担;切语言纯前端行为,不用重发请求。

### 13.2 后端:错误码而非文案

**错误码分段**(数字段位 + 语义 key 双轨,纯数字 key 可维护性差,必须配语义 key)。
**权威清单是 `backend/src/SmartAdmin.Core/ErrorCode.cs`** —— 那里每个成员都带 `[MsgKey]`,新增码按段落位;
本表只给分段规划,具体码值以代码为准。

| 段 | 用途 |
|---|---|
| 0 | 成功 |
| 40000–40999 | 认证与登录 |
| 41000–41999 | 权限与数据范围 |
| 42000–42999 | 用户/组织/角色/菜单 |
| 43000–43999 | 字典/配置 |
| 44000–44999 | 文件上传 |
| 45000–45999 | 消息通知 |
| 46000–46999 | 导入/导出 |
| 47000–47999 | 定时任务 |
| 48000–48999 | 请求参数 |
| 50000–50999 | 系统内部错误 |

消费者自己的错误码从 **60000** 起,定义成自己的 enum、成员挂 `[MsgKey]`,强转成 `ErrorCode` 抛出即可;
登记进 `ApplicationAssemblies`(或 `ErrorCodeEnums`)之后,统一信封会解析到它自己的 msgKey,不必抄一份异常过滤器。

```csharp
public enum ErrorCode { PasswordWrong = 40001, CaptchaExpired = 40002, UserNotFound = 42001 }
throw new AdminException(ErrorCode.PasswordWrong);          // 只抛码,不写中文
```
统一返回结构(`code` 给机器、`msgKey` 给前端 i18n、`message` 是后端兜底降级):
```jsonc
{ "code": 40001, "msgKey": "error.auth.passwordWrong", "args": {}, "message": "密码错误", "data": null }
```
- 后端**可选**内置默认多语言资源(`.resx`/json,`IStringLocalizer`),按 `Accept-Language` 兜底填 `message`,给非浏览器调用方(第三方直接调 API)。
- 浏览器端优先用 `msgKey` 走前端翻译,`message` 只作降级。

### 13.3 前端:vue-i18n
```
web/packages/admin/src/locales/
├─ zh-CN.ts      # 内置文案,嵌套键:{ menu: {...}, error: { auth: { passwordWrong: '密码错误' } } }
├─ en-US.ts      # 与 zh-CN 键对齐(parity.spec.ts 守)
├─ index.ts      # createI18n + registerLocales(把应用的 locales/ext 深合并进内置文案)
└─ menuTitle.ts  # 菜单标题翻译单源 translateMenuTitle + registerMenuTitles
```
- 视图 `catch` 后 `translateError(e)`:拿 `{msgKey, args}` → `t(msgKey, args)` 出本地化提示(**用语义 key,不用 `error.${code}` 纯数字**;数字码只对内核少量码有白名单兜底)。
- 语言持久化到 `stores/app` + localStorage;组件库(Naive UI)的内建语言包随之切换。

### 13.4 动态内容(数据库里的文本)怎么办
菜单名、字典项、系统配置这类**存在库里的文案**,两种策略,按需选:
- **默认(推荐,简单)**:库里存"翻译键",前端用 `t(key)` 渲染;键不存在就回退显示原文。零表结构改动。
- **进阶(可选)**:内置多语言表 `sys_i18n(resourceKey, culture, value)`,后台可维护;适合运营要在线改文案的场景。先做默认策略,进阶留作后续。

### 13.5 语言清单
内置 `zh-CN` + `en-US`。应用按 `src/locales/ext/<locale>/<命名空间>.ts` 放文件,经 `createSmartAdmin({ locales })` 深合并进这两种语言,可补键也可覆盖内置键;语言清单本身由内核决定。

### 13.6 OpenAPI → 前端代码生成(契约单源工具链)
- 定 **`openapi-typescript`(生成类型)+ `openapi-fetch`(轻量类型化请求客户端)**——依赖最轻,合"少依赖"理念;前端**不手写 DTO/接口类型**,人工只写业务 hooks/composables。
- 前端一条 `npm run gen:api`,从跑着的后端拉 `/openapi/v1.json`。
- **契约纪律**:生成物分两层。内核包的 `packages/admin/src/api/schema.d.ts` 与后端同仓同源,对着 MinimalHost 本地生成即用,导出为 `KernelPaths` / `KernelComponents`;应用对着自己的后端生成自己的 `src/api/schema.d.ts`(内核端点与应用端点都在里面),用 `createApiClient<paths>()` 建客户端。内置页调用的端点跟着后端版本走,所以 `smart-admin-web` 与 NuGet 包同号,模板精确钉版本(§17)。

---

## 14. 安全基线

必须成型的安全项(多数为 .NET 内置能力):

- **密码哈希**:PBKDF2(BCL `Rfc2898DeriveBytes`),存储格式含**算法版本 + 迭代次数 + 盐 + hash**,便于将来平滑升级算法。
- **Refresh Token**:数据库/缓存持久化(**存 hash 不存明文**),支持**轮换、吊销、复用检测**(旧 refresh 被重放即判风险、吊销该会话)。
- **JWT**:生产**必须**配置 `SecretKey`;开发密钥只允许 Development 环境自动生成(带醒目警告)。
- **登录防护**:验证码 + 登录失败锁定 + `RateLimiter`(登录接口更严)。
- **文件上传**:后缀白名单 + 大小限制 + **文件名重写** + **路径穿越防护** + **不以 Content-Type 作为唯一依据**。
- **授权默认拒绝**:后台业务接口默认需认证;匿名放行需显式 `[AllowAnonymous]`,只要求登录、不要求具体权限的端点挂 `[ActiveSession]`。
- **敏感字段脱敏**:日志里密码、token、密钥、手机号等脱敏。
- **CORS**:默认 `Api:Cors:AllowedOrigins` 为空 = **不放行任何跨源**,不是「允许本地开发源」。前后端同源部署压根不用配;真要跨源就显式列出来。

## 15. 会话与 Token 模型

支撑"在线用户 / 强退 / 权限变更即时生效"的基础(对应 §3.2 `Security:Session`、§6 授权管道):

- **AccessToken**:短期 JWT,**不落库**。
- **RefreshToken**:服务端保存 **hash**,不保存明文。
- **SessionId**:写入 JWT claim,作为**强退与在线用户的稳定标识**。
- **缓存 key**:`smart:session:{sessionId}` → 保存用户、设备、过期时间、状态。
- **强退**:删除/标记 session;权限过滤器**每次请求校验 session 状态**(失效即 401)。
- **Refresh 轮换**:每次刷新吊销旧 RefreshToken、签发新的。
- **单端模式**(`Session:Mode=Single`):新登录时吊销同用户其他 session。
- **`MaxConcurrent`**:超过并发端数时,按最早登录时间吊销最旧 session。

**实现要点**:RefreshToken 存 **SHA-256 十六进制**(高熵随机串,非密码,无需 PBKDF2);刷新用**条件更新**(仅当仍 Active 才置 Used)原子轮换,兼作并发双刷保护;**复用检测**:已 Used 的令牌再现即吊销**整个会话**(连坐刷新令牌 + 清缓存),攻击者与真用户一起下线(安全优先)。会话热路径:`ISessionService.IsActiveAsync` 先读 `smart:session:{sid}` 缓存(TTL=会话过期),未命中查库回填;`[RolePermission]` 管道对**超管与普通用户一律**校验会话状态(强退即 401)。刷新走**滑动续期**(会话过期跟到新刷新令牌过期)。审计字段 `CreateUserId/UpdateUserId` 由 SqlSugar AOP 从 `ICurrentUser` 自动填充(系统上下文留空)。

**部署边界(单节点 vs 多实例,重要)**:强退/权限吊销/登录锁定/验证码一次性的**即时性均依赖共享缓存**。默认 `MemoryCacheProvider` 是**进程内**:多副本负载均衡下,节点 A 的强退/降权只清了 A 的缓存,路由到节点 B 的请求仍读旧缓存(会话 TTL 可达 RefreshExpire 天级、权限/数据范围 TTL 至 `PermissionMinutes`),这些安全动作在 B 上**不即时生效**(`PermissionMinutes=0` 永不过期时 B 上永久保留旧权限)。**故:多实例部署必须配置分布式 `ICacheProvider`(如 `SmartAdmin.Caching.Redis`),否则强退/权限即时性、登录锁定、验证码一次性都只在单节点内成立。** 跨节点失效广播(利用 `IEventBus` 在副本间传播 session/perm/scope 失效)是 Redis 可选包/多实例路线的后续项。

## 16. 内置表清单(草案)

**表结构即长期契约,需尽早稳定**。内置表(前缀 `sys_`),按域分组:

| 域 | 表 |
|---|---|
| 组织与 RBAC | `sys_user`、`sys_role`、`sys_org`、`sys_position`、`sys_menu`、`sys_module`、`sys_user_role`、`sys_role_menu`、`sys_role_data_scope`、`sys_high_sensitivity_permission` |
| 会话与认证 | `sys_session`、`sys_refresh_token`、`sys_user_external`、`sys_totp_recovery_code`、`sys_password_history` |
| 字典与配置 | `sys_dict_type`、`sys_dict_item`、`sys_config` |
| 日志 | `sys_login_log`、`sys_op_log`、`sys_exception_log` |
| 文件 | `sys_file` |
| 通知 | `sys_notice`、`sys_notice_receiver`、`sys_notice_read` |
| 定时任务 | `sys_job`、`sys_job_log`、`sys_job_lock`、`sys_job_node` |
| 运行时 | `sys_schema_version`、`sys_worker_lease` |

表名以代码为准(操作日志表叫 `sys_op_log`),内核共 31 张表。
逐字段定义看 `backend/src/SmartAdmin.Services/Entities/`,那里才是契约本身。

> 多应用门户(§4/§6):`sys_module` 为模块/应用表;`sys_menu` 增 `ModuleId`(仅顶级目录挂模块)+ 前端展示列 `Path`/`Component`/`Icon`/`Visible`;`sys_user` 增 `DefaultModuleId`(每用户默认应用)。模块访问权由菜单授权反推,不建 `sys_role_module`/`sys_user_module` 派生表。

> 均继承 `BaseEntity`/`DataEntity`(§5.6);字段级设计在 M1 前定稿,定稿后视为对外契约,破坏性变更只在主版本。

## 17. 版本与发布策略

- **主版本号 = 内核所用的 .NET 主版本**:10.x 对应 .NET 10;TFM 跟随 .NET LTS 滚动升级,下一次大版本随下一个 LTS(中间的 STS 版本不单独占号)。换 .NET 大版本对消费者本就是一次破坏性升级,主版本号在这里如实表达它。
- 次版本号加功能、修订号修 bug。破坏性变更尽量攒到换 .NET 大版本时一起发;周期内确需破坏的在次版本发,并在 CHANGELOG 对应段落顶部加粗提示。
- 所有 `SmartAdmin.*` 包、npm 包 `smart-admin-web` 与 `SmartAdmin.Templates` **同版本发布**,不做独立版本号;后端仓用 `Directory.Build.props` / `Directory.Packages.props` 统一版本与依赖版本,发版号由 tag 经 `-p:Version` 注入。
- 可选包依赖同号的核心包(pack 时 ProjectReference 转成同版本的 PackageReference)。
- 发版由 GitHub Actions 完成(`docs/releasing.md`):`v*` tag 打在 `main` 上,`release` workflow 打包并经 Trusted Publishing 推 nuget.org、发 npm。前端模板对 `smart-admin-web` 精确钉版本:内置页照同一版后端的接口写,浮动范围会装到比 NuGet 新的包。

## 18. 最小验收闭环

这套端到端场景比模块清单更能指导开发——**跑通它 = 首发达标**:

1. 新建空 ASP.NET Core 项目;
2. 安装 `SmartAdmin` NuGet 包;
3. `Program.cs` 里调用 `AddSmartAdmin` / `MapSmartAdmin`(§3.1);
4. `dotnet run`;
5. 控制台打印首次超管账号/密码;
6. 打开 Vue 前端;
7. 登录成功;
8. 进入用户管理;
9. 新增机构、角色、用户;
10. 给角色授权菜单和数据范围;
11. 用新用户登录;
12. 验证菜单权限与数据权限均生效;
13. 修改系统配置/字典;
14. 查看操作日志与登录日志;
15. 强退某在线用户;
16. 刷新页面后动态路由仍正常。
