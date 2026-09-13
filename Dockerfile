# SmartAdmin 后端镜像。构建 backend/samples/MinimalHost —— 零配置的最小宿主。
#
# 为什么用 MinimalHost 而不是 templates/content/smart-app：
#   模板工程是 `dotnet new` 的模板内容，包版本是占位符 SMART_PKG_VERSION，实例化之后才能还原，
#   而且装的是 nuget.org 上已发布的内核。MinimalHost 使用 ProjectReference 从源码构建，镜像里就是当前提交的内核。
#   消费者自己的 Dockerfile 位于 templates/content/smart-app/Dockerfile（从 NuGet 装内核）。
#
# MinimalHost 的 appsettings.json 没有 SmartAdmin 配置节（它是"零配置即可运行"的活证据），
# 所以所有配置都通过 docker-compose.yml 的 SmartAdmin__Xxx__Yyy 环境变量注入——这同时也演示了
# 文档站「部署」一节描述的双下划线配置方式(https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/)。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet.config 位于仓库根目录（<clear /> 掉机器级的源，只用 nuget.org）；dotnet 会从项目目录向上查找它。
# 文件名大小写必须与仓库中的完全一致——Windows 不区分大小写，但 Linux 构建上下文会直接报"未找到"。
COPY NuGet.config ./

# 先只拷工程文件与版本清单，单独 restore 一层。依赖没动时这层直接命中缓存，
# 改一行业务代码就不必重新下一遍包。要是把 COPY backend/ 提到 restore 前面，
# 任何一次源码改动都会让还原层失效。
# 版本集中在 Directory.Packages.props，两份 props 必须跟工程文件一起进来。
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/SmartAdmin.slnx backend/
COPY backend/src/SmartAdmin/SmartAdmin.csproj                             backend/src/SmartAdmin/
COPY backend/src/SmartAdmin.Core/SmartAdmin.Core.csproj                   backend/src/SmartAdmin.Core/
COPY backend/src/SmartAdmin.SqlSugar/SmartAdmin.SqlSugar.csproj           backend/src/SmartAdmin.SqlSugar/
COPY backend/src/SmartAdmin.Services/SmartAdmin.Services.csproj           backend/src/SmartAdmin.Services/
COPY backend/src/SmartAdmin.AspNetCore/SmartAdmin.AspNetCore.csproj       backend/src/SmartAdmin.AspNetCore/
COPY backend/src/SmartAdmin.Caching.Redis/SmartAdmin.Caching.Redis.csproj backend/src/SmartAdmin.Caching.Redis/
COPY backend/src/SmartAdmin.Excel/SmartAdmin.Excel.csproj                 backend/src/SmartAdmin.Excel/
COPY backend/src/SmartAdmin.Auth.GitHub/SmartAdmin.Auth.GitHub.csproj     backend/src/SmartAdmin.Auth.GitHub/
COPY backend/src/SmartAdmin.Auth.WeChat/SmartAdmin.Auth.WeChat.csproj     backend/src/SmartAdmin.Auth.WeChat/
COPY backend/src/SmartAdmin.Auth.WeCom/SmartAdmin.Auth.WeCom.csproj       backend/src/SmartAdmin.Auth.WeCom/
COPY backend/src/SmartAdmin.Auth.DingTalk/SmartAdmin.Auth.DingTalk.csproj backend/src/SmartAdmin.Auth.DingTalk/
COPY backend/samples/MinimalHost/MinimalHost.csproj                       backend/samples/MinimalHost/
RUN dotnet restore backend/samples/MinimalHost/MinimalHost.csproj

COPY backend/ backend/

RUN dotnet publish backend/samples/MinimalHost/MinimalHost.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# 两个数据目录在镜像中预先创建并设置所有权——compose 通过**命名卷**挂载它们：
# 命名卷首次挂载时会继承镜像目录中的内容和所有权，这是非 root 应用用户能写入的唯一方式。
# （绑定挂载会用宿主机的所有权覆盖，导致非 root 用户无法写入——这是容器化上传/SQLite 时最常见的坑。）
#   /app/data    SQLite 数据库文件 + 开发环境 JWT 签名密钥（相对于 ContentRoot，即 /app）
#   /data/upload 上传文件。**刻意放在 wwwroot 外部**：一旦有人在此镜像中添加 UseStaticFiles()，
#                wwwroot 下的上传目录将被匿名访问——这是一个认证绕过（见 https://smartcode-x.github.io/SmartAdmin/zh/guide/deployment/route-a 的告警）。
RUN mkdir -p /app/data /data/upload && chown -R $APP_UID:$APP_UID /app/data /data/upload

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 此处不设置 HEALTHCHECK：aspnet 运行时镜像既没有 curl 也没有 wget，设了也只会始终失败。
# 健康检查留给编排层探测 /health（匿名端点，compose 和 k8s 均可直接使用）。
ENTRYPOINT ["dotnet", "MinimalHost.dll"]
