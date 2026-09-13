# ADR 0007 — 外部登录品牌化 UI + GitHub / 个人微信可选包

- 状态:已采纳(2026-07-31)
- 相关:[[ADR-0002]](外部登录 / SSO 骨架,本 ADR 不推翻);前端实现见 `web/packages/admin/src/utils/oauthBrand.ts` 与 `web/packages/admin/src/assets/oauth/`

## 背景

[[ADR-0002]] 已落地 `IExternalAuthProvider`、内置 OIDC、企业微信 / 钉钉可选包、`GET providers` 驱动登录按钮、个人中心绑定,以及未绑定默认拒绝 + per-provider JIT。不足在两处:登录页呈现偏功能按钮,不像产品站的「其他方式登录」品牌圆钮;GitHub、个人微信这类常用互联网身份源没有官方包。本 ADR 冻结 UI 语言、首批 provider 与交付切分;协议骨架、票据交换、绑定表、默认拒绝策略全部沿用 0002。

## 决策

1. **交付形态 = 前端呈现升级 + 新可选包。** 厂商 SDK 与品牌资源不进内核四包;新身份源按 `SmartAdmin.Auth.WeCom` / `.DingTalk` 的成法做:独立 NuGet、仅 Core + Microsoft.\*、`HttpClient` 构造注入(生产用 `IHttpClientFactory`)、`AddSmartAdminXxxAuth` 前置注册;登录按钮只渲染 providers 接口返回的已启用项。
2. **首批身份源 = GitHub + 个人微信。** QQ / Gitee 只在前端 brand map 占位图标,不做后端包。官方包的 provider `Code` 硬固定(`github` / `wechat`),options 不暴露 Code;要第二套同厂商应用就自写 `IExternalAuthProvider`。企业微信仍是 `wecom`,与个人微信的图标和 code 绝不混用。
3. **GitHub:** OAuth App,Subject = 数字 `id` 字符串化,scope 只申请 `read:user`,不调 `/user/emails`,不按邮箱并号。
4. **个人微信:** 开放平台网站应用 `qrconnect`,Subject **只**取 `unionid`,响应里没有 unionid 即交换失败,不降级为 openid(否则 `(Provider, Subject)` 唯一绑定会身份漂移);本批不调 userinfo,外部用户显示名恒为 null。
5. **未绑定策略沿用 0002 默认拒绝。** JIT 开户不是 appsettings 项,而是 `sys_config` 运营键 `sys.externalauth.{code}.provisioning`(可选 `defaultRoleIds` / `defaultOrgId`),示例宿主默认不打开。
6. **两种显示名分离:** provider 的 `DisplayName`(登录圆钮 tooltip;GitHub 默认「GitHub」、微信默认「微信」,空值回退默认,禁止空串)与 `ExternalIdentity.DisplayName`(绑的是谁;GitHub 取 `login` → `name` → null)。
7. **UI 语言:** 纯图标圆钮,`title` = displayName;登录页最多平铺 4 个,第 5 个起进「…」菜单;顺序严格按 providers 接口返回序,后端不加排序字段。图标解析顺序:前端 brand map 命中 code → 精修 SVG;否则 `icon` 为 Iconify 名称 → 离线渲染;否则首字母 / 通用 SSO 标。不接受 URL、data URL、远程 SVG。
8. **绑定页** = 已启用 providers ∪ 当前用户已有绑定的 provider(即便运营已关):已停用行显示「已停用」,允许解绑,禁止再绑定;不做 4 个截断,不做禁用时级联删绑定。
9. **系统配置独立页签「第三方登录」:** 键空间 `sys.externalauth.{code}.*`,GroupCode `externalauth`,主能力是「登录页显示」开关(`enabled`,缺省 true);密钥与连接仍只在 appsettings。

## 后果

- 登录 / 绑定的信息架构与 0002 一致,只扩展了呈现与官方 provider 集合;消费者装包 + 配 appsettings 即亮圆钮,未装或未启用则不出现。
- 品牌 SVG 留在前端模板,不进后端 NuGet 分发面。
- 包内必须有 handler 级测试覆盖 mapping 与脱敏;前端用 vitest 覆盖平铺 / 溢出、图标解析与绑定页合并。

## 非目标

微信公众号网页授权与小程序登录、默认 JIT 开户、按邮箱并号、内核引入厂商官方 SDK、一锅端 Google / Microsoft / Apple 图标库。
