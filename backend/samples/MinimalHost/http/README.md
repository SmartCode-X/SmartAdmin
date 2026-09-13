# 示例宿主的 `.http` 请求样例

一套能直接点着跑的 API 样例:新人拿到仓库,五分钟内可以亲手把内核这套 API 走一遍;同时它也是最便宜的冒烟脚本——起完服务挨个点一遍,哪块坏了当场就看得见。

不追求覆盖每个端点,每个模块只挑真正会被手动调的那几条。完整契约看 `/openapi/v1.json`。

## 先把宿主跑起来

```bash
dotnet run --project backend/samples/MinimalHost
```

零配置即跑:默认 SQLite,表由 CodeFirst 自动建,种子数据自动播。监听 `http://localhost:5100`(见 `Properties/launchSettings.json`),所有文件顶部的 `@host` 就是它。

**首次启动会在控制台醒目打印一次随机超管密码**,账号是 `superAdmin`。这个密码只在真正建号的那次启动打印,后面再启动不会重复打印,所以看到了就先存下来。想要一个固定值,就在 `backend/samples/MinimalHost/appsettings.Development.json`(从同目录的 `.example` 复制)里设 `SmartAdmin:Seed:AdminPassword`,然后删掉 `data/` 下的库文件重启——已经建过号的库改这个配置不生效。

拿到密码后,把每个 `.http` 文件顶部的 `@password` 换成它。

## 用什么打开

| 工具 | 怎么用 |
| --- | --- |
| VS Code | 装 [REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) 扩展,每条请求上方会出现 `Send Request` |
| JetBrains Rider / IntelliJ | 原生支持,请求左边有绿色运行按钮 |
| Visual Studio 2022 | 17.8+ 原生支持,请求上方有 `发送请求` 链接 |

## 怎么跑

1. **先跑文件顶部那条「登录」**。它用 `# @name login` 命名,后面每条请求的 `Authorization` 头都从它的响应里取令牌:`{{login.response.body.$.data.accessToken}}`。
2. 再按需要点后面的请求。有前置依赖的地方(比如字典必须先建类型才能加项)都在注释里点明了。
3. 带 `<粘贴……>` 占位值的 `@` 变量需要手工填一次:雪花 Id、会话 Id 这些拿不到就没法往下走的值,注释里写了从哪一条的返回里取。

### 为什么每个文件都重复一条登录

`.http` 的请求变量(`{{login.response...}}`)**作用域只到本文件**——VS Code REST Client 和 VS 2022 都是这个规矩,跨文件引用不到别的文件里的命名请求。所以每个文件自带一条登录,是让每个文件都能独立点开就跑,不是复制粘贴偷懒。

### Rider / IntelliJ 用户注意

Rider 的 HTTP Client 不认 `{{login.response.body.$....}}` 这种请求变量语法(那是 REST Client 和 VS 2022 的写法),它走的是响应处理脚本。在 Rider 里跑,把登录请求改成下面这样,再把各请求的 `Authorization` 头换成 `Bearer {{token}}`:

```http
POST {{host}}/api/v1/auth/login
Content-Type: application/json

{ "account": "{{account}}", "password": "{{password}}" }

> {% client.global.set("token", response.body.data.accessToken); %}
```

或者更省事:跑一次登录,把响应里的 `accessToken` 复制出来,在文件顶部加一行 `@token = <粘贴>`,请求里用 `{{token}}`。

## 文件清单

建议按这个顺序跑一遍:

| 文件 | 内容 | 要不要令牌 |
| --- | --- | --- |
| `meta.http` | 健康检查、错误码目录、站点信息、OpenAPI | 否 |
| `auth.http` | 验证码、登录、探针、刷新、登出 | 自带登录 |
| `personal.http` | 个人中心、工作台统计、我的通知 | 是 |
| `user.http` | 用户增删改查、重置密码、导入导出 | 是 |
| `role.http` | 角色、菜单授权、数据范围、授权用户 | 是 |
| `menu.http` | 可授权路由清单、菜单树与增删改、应用(模块) | 是 |
| `org.http` | 机构树、职位 | 是 |
| `dict.http` | 字典类型与字典项(有先后顺序) | 是 |
| `config.http` | 系统配置、密码策略、批量回写 | 是 |
| `job.http` | 定时任务、cron 预览、执行记录、监控面板 | 是 |
| `log.http` | 操作/登录/异常日志的查询、导出、清空 | 是 |
| `file.http` | 上传、下载、签名直链、分片初始化 | 是 |
| `ops.http` | 在线会话与强退、清缓存、服务器监控、回收站、发通知 | 是 |

`meta.http` 一条令牌都不需要,适合当第一站:它全绿就说明宿主起来了、表建好了、种子跑过了。

## 几件会踩到的事

- **清空日志、彻底删除、强制下线**这类请求是不可逆的,注释里都标了,别顺手一路点下去。
- 超管跑什么都通,因为 `sadm` 声明直接放行权限校验。要验普通账号的权限效果,得先在 `role.http` 里给角色授菜单,再拿那个账号登录。
- `[RequireReauth]` 的端点(建用户、改配置、彻底删除等)在 TOTP 关闭时(默认)是空操作;开了 TOTP 之后,得先 `POST /api/v1/auth/reauth` 拿到再认证授予,否则 403。
- 认证端点有更严的限流(默认单 IP 每分钟 20 次),反复试密码试到 429 是正常的,等一分钟。
- 连续输错密码会锁账号(默认 5 次 / 锁 10 分钟)。
