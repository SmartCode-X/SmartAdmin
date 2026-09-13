# SmartApp

基于 **SmartAdmin** 内核的后台 host,由 `dotnet new smart-app` 生成。已接线一个机构隔离示例业务模块(`Modules/SampleDoc*`)。

## 运行

```bash
dotnet run
```

- 零配置默认用 SQLite(相对路径落在 ContentRoot),自动建表 + 种子。
- `Properties/launchSettings.json` 把 `ASPNETCORE_ENVIRONMENT` 钉成 `Development` —— **别删**:落到 `Production` 时 CodeFirst 自动建表默认关闭(生产不该自动改表),首启会因为「种子要写的表不存在」直接启动失败。生产建表走 `SmartAdmin:Database:EnableCodeFirstInProduction` 或由 DBA 预建,见[部署指南](https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/)。
- **首次启动控制台会打印随机超管密码**,用它登录。
- 换数据库:改 `appsettings.json` 的 `SmartAdmin:Database` 节(DbType + 连接串),支持 SQLite / MySQL / SqlServer / PostgreSQL。
- 健康检查 `/health`、`/health/ready`;开发期 OpenAPI 契约 `/openapi/v1.json`。
- 其余常用配置键(JWT 密钥、CORS、上传目录、WorkerId)在 `appsettings.json` 里以注释形态列好了。

## 上线

`npm run dev` 的 `/api` 代理只在开发期存在;部署要么让本 host 用 `UseStaticFiles()` 顺带托管前端产物(此时**必须**把 `SmartAdmin:Upload:RootPath` 挪出 `wwwroot`),要么交给 nginx 反代。生产还必须显式配 `SmartAdmin:Jwt:SecretKey`。完整步骤见[部署指南](https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/)。

### Docker

本目录已带一份 `Dockerfile`(多阶段、非 root、8080),`docker build -t myapp .` 即可。要注入哪些环境变量,文件末尾列全了;全栈编排(后端 + Caddy 托管的前端 + MySQL + Redis)可参照内核仓库的 `docker-compose.yml`。

## 起前端

后台界面是 npm 包 `smart-admin-web`(Vue 3 + Naive UI),与本工程引用的 `SmartAdmin` NuGet 包同号。拉一份薄壳模板到本工程的 `web/` 下即可(degit 只取文件、不带 git 历史,拉下来就是你自己的):

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web && npm install && npm run dev
```

dev server 起在 `5173`,`/api`、`/openapi`、`/hub` 反代到本 host 的 `5100`(改目标用环境变量 `SMART_API_TARGET`)。接口类型是从跑着的后端生成的:`npm run gen:api` 抓 `/openapi/v1.json` 写出 `src/api/schema.d.ts`,再把 `src/api/client.ts` 里的 `KernelPaths` 换成生成出来的 `paths`。自己的页面放 `src/views/`,文案放 `src/locales/ext/`,组件与 API 原语从 `smart-admin-web` 导入。

升级时把 `SmartApp.csproj`、`Tests/Tests.csproj` 里的 `SmartAdmin*` 包和 `web/package.json` 里的 `smart-admin-web` 改成同一个版本号,见[升级到新版本](https://smartcode-x.github.io/SmartAdmin/zh/guide/upgrade)。

## 测试

```bash
dotnet test Tests
```

`Tests/` 是独立工程(主工程已把它排除在编译之外),用 `WebApplicationFactory` 起整个 host、登录超管、把示例模块的 CRUD 走一遍。它守的是接线:表建没建、路由挂没挂、权限码放没放行。加自己的模块时复制 `SampleDocCrudTests.cs` 改路由和字段。宿主、库、登录、信封解析都来自 `SmartAdmin.Testing` 包(`AppFactory : AdminAppFactory<Program>` 只是薄薄一层);设 `SMART_TEST_DBTYPE=MySql` 等环境变量,同一套用例就跑在别的数据库上。

## 加一个业务模块

复制 `Modules/` 下的四件套改名即可(示例 `SampleDoc` 是机构隔离表,继承 `DataEntity`;不需机构隔离的表改继承 `BaseEntity`):

1. `{实体}.cs` —— `[SugarTable]` 实体。
2. `I{实体}Service.cs` + `{实体}Service.cs` —— 业务服务(方法 `virtual`,构造注入 `IRepository<{实体}>`)。
3. `{实体}Controller.cs` —— `[ApiController]` + `[Route]`,每个 action 挂 `[RolePermission]`,返回 `Result<T>`。
4. 在 `Program.cs` 追加一行 `builder.Services.TryAddScoped<I{实体}Service, {实体}Service>();`。

实体在本程序集内,`AddSmartAdmin(..., o => o.ApplicationAssemblies.Add(...))` 已登记 → 自动建表、控制器自动挂路由。**权限码 = 规范化路由**(如 `GET:/api/v1/sample/doc`),普通用户经角色-菜单授权;超管放行。
