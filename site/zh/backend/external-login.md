# 外部登录（SSO）

第一次用企业微信扫码进来的人会被拒绝，报 `OAuthAccountNotBound`：默认策略如此，不是配错了。内核一贯的立场是账号只由管理员开，没有自注册，外部身份也照这条办。要让 SSO 自己开户，得按 provider 显式打开那个开关。

## 五个 provider，两种打包方式

| provider | 装在哪 | 对接方式 |
| --- | --- | --- |
| `oidc` | 内核内置（AspNetCore 层） | 标准 OIDC，通吃 Keycloak、Entra、Authing、Auth0 |
| `wecom` | 可选包 `SmartAdmin.Auth.WeCom` | 桌面浏览器扫码登录，企业微信客户端里走网页授权，按 User-Agent 自动选 |
| `dingtalk` | 可选包 `SmartAdmin.Auth.DingTalk` | 钉钉 PC 扫码 / 网页授权 |
| `github` | 可选包 `SmartAdmin.Auth.GitHub` | GitHub OAuth App，scope 仅 `read:user` |
| `wechat` | 可选包 `SmartAdmin.Auth.WeChat` | 微信开放平台网站应用扫码（`snsapi_login`），身份只取 `unionid` |

内置 OIDC 零新增依赖：发现文档、JWKS、`id_token` 验签全用 JwtBearer 已经传递进来的 `Microsoft.IdentityModel.*`。四个厂商包各自只引 `Core` 加 Microsoft.\*，用裸 `HttpClient` 对接厂商 API。所以它们能独立发版，也不会把厂商 SDK 拖进内核。

四个可选包还是老规矩，在 `AddSmartAdmin()` 之前注册。它们按 `Code` 和内置 provider 并存：

```csharp
builder.Services.AddSmartAdminWeComAuth(builder.Configuration);
builder.Services.AddSmartAdminDingTalkAuth(builder.Configuration);
builder.Services.AddSmartAdminGitHubAuth(builder.Configuration);
builder.Services.AddSmartAdminWeChatAuth(builder.Configuration);
builder.Services.AddSmartAdmin(builder.Configuration);
```

## 配置分两处放

密钥和运营项不放在一起，这是刻意的。

**连接与密钥走 `appsettings`**，和 Database、Jwt、Email 一个路子，密钥不进库：

```jsonc
{
  "SmartAdmin": {
    "ExternalAuth": {
      "CallbackBaseUrl": "https://admin.example.com",
      "Oidc": [ { "Code": "keycloak", "Authority": "...", "ClientId": "...", "ClientSecret": "..." } ],
      "WeCom": { "CorpId": "...", "AgentId": "...", "CorpSecret": "..." }
    }
  }
}
```

`CallbackBaseUrl` 只填后端对外的根地址，回调路径 `/api/v1/auth/external/{provider}/callback` 由内核接在后面。开发环境不配会回退到请求主机；生产环境必须配，因为 Host 头能伪造。填错了厂商照样跳转，它们只校验域名，最后落在一条不存在的路径上，看着就是「授权完什么也没发生」。所以启动时先校验：整条回调地址、前端结果页 `FrontendResultPath`、带查询串或 `#` 片段、不是 `http(s)` 绝对地址，都会让应用拒绝启动。网关子路径可以，比如 `https://gw.example.com/admin`。这时前端的 `apiBase` 也要走同一个前缀。内核靠两个 cookie 确认回调与认领来自发起登录的那个浏览器，cookie 的 Path 跟着前缀走（`/admin/api/v1/auth/external`）。前端绕开前缀调接口，cookie 就带不上，登录回调或认领待绑定会报 40014。

`GET /api/v1/auth/external/providers` 只回非密钥字段（code、显示名、图标），够前端点亮按钮就行。

**运营项走 `sys_config`**，配置页上运行时可改，按 provider code 组键：

| 配置键 | 默认 | 管什么 |
| --- | --- | --- |
| `sys.externalauth.{code}.enabled` | 启用 | 这个 provider 开不开 |
| `sys.externalauth.{code}.provisioning` | 拒绝 | 未绑定账号首次登录时开不开户 |
| `sys.externalauth.{code}.linkByAccount` | 关 | 未绑定的外部身份按账号名关联同名本地账号 |
| `sys.externalauth.{code}.defaultRoleIds` | 空 | 自动开户时给什么角色 |
| `sys.externalauth.{code}.defaultOrgId` | 空 | 自动开户时落哪个机构 |

一个键都不配，默认行为就是**启用 + 拒绝开户**。只动 `appsettings`，就能跑起一套绑定优先的 SSO。

`enabled` 在配置中心「第三方登录」页的卡片上开关，企业微信卡片还多一个「按账号自动关联」，对应它的 `linkByAccount`。其余的键不预置，别的 provider 的 `linkByAccount` 也算在内。要用时到「其他配置」新增，分组填 `externalauth`，保存后列在「第三方登录」页底部的「本组其它配置」里。

这几个键的读取收口在 `ISysUserExternalService`，控制器和 `AuthService` 都只调它，不各自散读配置键。

::: tip 没有 provider 管理页，这是有意的
后端不建 provider 表、不建管理页。厂商密钥本质是部署基建，入库要加密存储、脱敏、再配一套 CRUD，攻击面和工作量都不划算。将来真要一个独立的 Provider 管理页，前端叠一个就行，后端不用动。
:::

## 未绑定的账号怎么办

`sys_user_external` 表按 `(Provider, Subject)` 唯一，记的是「哪个外部身份对应哪个本地用户」。首次外部登录时查不到绑定，这个 provider 开了 `linkByAccount` 就先按账号名关联，没开或没关联上，再由 `provisioning` 决定：

- **拒绝**（默认）：内部抛 `OAuthAccountNotBound`（40016），但登录回调不会把这个错误直接甩给用户——它转成一张「待认领」的一次性票据（`pendingLink`），引导用户改走账密或短信登录；登录成功后调 `POST pending-link/claim` 带上这张票据，才把外部身份绑到当前账号（认领要求与发起登录同一浏览器，防钓鱼抢绑）。反过来也行：先有本地账号，再去个人中心用 `POST {provider}/bind` 主动发起绑定。
- **自动开户**（JIT）：建一个本地账号，随机口令占位、不要求改密，角色和机构取上面那两个配置键。

### 按账号名关联

本地账号本来就是企业微信账号时，待认领是多余的一步：员工手上根本没有密码可填。把 `sys.externalauth.{code}.linkByAccount` 设成 `true` 之后，外部身份的标识（企业微信就是 `userid`）和某个本地账号完全相同，首次登录就绑上并直接进。以后只认绑定，不再按名字找。比较口径和账号密码登录查账号一样，大小写敏不敏感看数据库排序规则。企业微信的 `userid` 本身不区分大小写，两边写法最好统一。企业微信卡片上打开它会先弹确认框；别的 provider 照前面说的，到「其他配置」加键。

超级管理员、已停用的账号、已经绑过这个 provider 的账号，永不自动关联；找不到同名账号也一样，都交回 `provisioning`。超级管理员要用外部登录，只能自己去个人中心绑。和自动开户同时开着时，先关联，关联不上再开户。按邮箱、手机号这类别的口径关联，覆写 `AuthService.LinkByAccountAsync` 或 `ResolveExternalUserAsync` 即可，不必改内核。

::: warning 打开前先确认两边是同一批人
打开它，「这个人是谁」就交给 IdP 的账号来认了。

- 两边账号得是同一套来源、同一批人，比如本地账号就是从企业微信通讯录同步来的。各自建号的，同名未必同人，关联上就是张冠李戴。这是最现实的风险，开之前拿两边名单核一遍。
- 谁能改 IdP 里的账号名，谁就能让一个人登成同名本地账号。企业微信里只有管理员和通讯录同步接口改得了成员账号，普通成员自己改不了。企业内部，这项权限通常就在 IT 手里。
- 企业微信里给谁改了账号，本地要跟着改账号或解绑重绑。不然改名后的新账号下次首次登录，会去匹配同名的另一个本地账号。
- 只给标识由企业统一分配、用户自己选不了的 provider 开。企业微信的 `userid` 合适：非企业成员只拿得到 openid，互联企业的成员是 `CorpId/userid` 形式，都对不上普通账号。钉钉、微信、GitHub 的标识是 unionid 或数字 id，开了也对不上。允许自助注册、`sub` 就是用户名的 OIDC IdP，不要开。
:::

## 在企业微信客户端里登录

同一个 `wecom` provider 出两种授权页，看发起授权那次请求的 User-Agent 选。带 `wxwork` 的是企业微信客户端的内置浏览器，桌面端和手机端都算。扫码页在那里用不了，于是走 OAuth 网页授权，`scope` 是 `snsapi_base`，静默授权，不弹确认页。其余情况一律是扫码登录。两种授权带回的 `code` 都拿去调 `auth/getuserinfo` 换 `userid`，后面的流程完全一样。

要让员工在企业微信里点开应用就进系统，企业微信后台配两处：

- 应用主页指向站点。
- 可信域名覆盖 `CallbackBaseUrl` 的域名，带端口的话端口也要登记，否则跳转时报 `redirect_uri` 参数错误。

前端包的内置登录页在企业微信客户端里会自动发起一次企业微信登录，每个浏览器会话只发一次。登录失败，或者主动退出后回到登录页，就停在那里让人自己选，不会来回跳。

首次登录照样要过未绑定这一关。默认拒绝，员工会落回登录页走待认领。本地账号就是企业微信账号的，到「第三方登录」页的企业微信卡片上打开「按账号自动关联」。员工第一次点开就进，不用拿密码认领。本地还没有账号的，打开自动开户。

## 端点

都挂在 `api/v1/auth/external` 下：

| 端点 | 用途 |
| --- | --- |
| `GET providers` | 列可用 provider，前端据此渲染登录按钮 |
| `GET providers/all` | 管理端：列出全部已注册 provider（含已禁用），带启用状态和按账号关联开关，供系统配置页的卡片开关 |
| `GET {provider}/authorize` | 换取跳转地址，带上一次性 state |
| `GET {provider}/callback` | 厂商回调落点 |
| `POST exchange` | 用一次性票据换令牌 |
| `POST pending-link/claim` | 认领未绑定的外部身份：账密/短信登录成功后，把回调阶段解析出的外部身份绑到当前用户 |
| `GET bindings` | 当前用户已绑定的外部身份 |
| `POST {provider}/bind` | 【个人中心】发起绑定一个外部身份 |
| `DELETE {provider}/binding` | 【个人中心】解绑一个外部身份 |

`state` 和一次性票据都复用短信验证码那套成法：进缓存、`GetAndRemoveAsync` 原子取删，单次有效。`state` 只用字母和数字，企业微信和微信的 OAuth 只收这个字符集。

外部登录解析出 `SysUser` 之后，接的是 `AuthService.CreateTokenAsync`。建会话、发令牌这段尾链，和账密登录、短信登录完全共用。所以会话并发策略、强退、刷新令牌轮换，对它一视同仁。

## 回调换令牌示例

`authorize` 和 `callback` 都是浏览器整页跳转，不是前端能 `fetch` 的 JSON 接口。`authorize` 302 到 IdP 的授权页；IdP 验证完用户后回跳 `callback`，`callback` 再 302 回前端结果页（默认 `FrontendResultPath`，即 `/oauth/callback`），查询串上带着下一步要用的东西：

```
成功：GET /oauth/callback?ticket=<一次性票据>
失败：GET /oauth/callback?error=40015
```

前端在结果页里认到 `ticket`，拿它去换令牌，这一步才是真正能 `fetch` 的接口：

```bash
curl -X POST http://localhost:5100/api/v1/auth/external/exchange \
  -H "Content-Type: application/json" \
  -d '{"ticket":"<回调带回来的一次性票据>"}'
```

响应信封和[账密登录](/zh/guide/getting-started)同一个形状：

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

票据一次性：`exchange` 内部用 `GetAndRemoveAsync` 原子取删，重复换第二次会拿到 `OAuthStateInvalid`（40014）。

## 错误码

| 码 | 名 | 什么时候 |
| --- | --- | --- |
| 40013 | `OAuthProviderDisabled` | 这个 provider 被运营开关关了 |
| 40014 | `OAuthStateInvalid` | state 对不上或已被消费 |
| 40015 | `OAuthExchangeFailed` | 向厂商换令牌失败 |
| 40016 | `OAuthAccountNotBound` | 没绑定，且这个 provider 不许自动开户 |
| 40017 | `OAuthAlreadyBound` | 这个外部身份已经绑在别的账号上 |

按[前后端契约](/zh/frontend/api-contract)的规矩，这几个码在两份语言包里都要配上对应的 `msgKey` 文案。漏一个，后端的一致性测试就直接变红。
