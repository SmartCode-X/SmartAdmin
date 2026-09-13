# 可选安全 — 独立配置键（定稿）

- 状态：**已定键名**（2026-07-30）
- 决策：[ADR 0006](../adr/0006-general-admin-optional-security.md)
- 绑定类型：`backend/src/SmartAdmin.Core/Options/AdminSecurityOptions.cs`
- 网页自审（入口与勾选表）：[security-optional-ui-checklist.md](security-optional-ui-checklist.md)

配置节根：`SmartAdmin:Security`。  
**默认全部宽松 / 可关**，零配置可跑；需要时再显式打开。

---

## 1. 产品键（请只用这些）

### 1.1 TOTP — `SmartAdmin:Security:Totp`

| 配置键 | 类型 | 默认 | 含义 |
|--------|------|------|------|
| **`sys.security.totp.enabled`**（SysConfig） | bool | `false` | **运行时总闸**（与登录验证码同款）。配置中心 → 安全策略可即时开关，无需改 appsettings |
| **`sys.security.totp.requireForSuperAdmin`**（SysConfig） | bool | `false` | 运行时：超管是否必须第二因子 |
| `Totp:Enabled`（appsettings） | bool | `false` | **部署地板**：为 `true` 时始终开能力（UI 关不掉）；默认 false，以 SysConfig 为准 |
| `Totp:RequireForSuperAdmin`（appsettings） | bool | `false` | SysConfig 键缺失时的回退 |
| `Totp:Issuer` | string | `SmartAdmin` | Authenticator 展示的 issuer |
| `Totp:ChallengeTtlSeconds` | int | `300` | 登录/挑战有效期（秒） |
| `Totp:ReauthWindowMinutes` | int | `5` | 高危操作再次确认窗口（分钟） |
| `Totp:RecoveryCodeCount` | int | `10` | 每次绑定生成的恢复码个数 |

**账号级：** 用户表 `ForceTotp`（或等价字段）= 管理员要求该用户必须 MFA；与全局键正交。

**绑定路径（ADR 0006）：** 用户自助 `POST /api/v1/auth/mfa/bind/start`（账号+密码）→ `complete`；管理员清除 `POST /api/v1/sys/mfa/clear`。  
**已拆除：** `sys/mfa/invite`、`auth/mfa/emergency-reset`、InitGrant 绑定 token。

### 1.2 会话 — `SmartAdmin:Security:Session`

| 配置键 | 类型 | 默认 | 含义 |
|--------|------|------|------|
| `Session:CookieMode` | bool | `false` | `true` = refresh 仅 HttpOnly Cookie + CSRF；`false` = body/localStorage 兼容 |
| `Session:CookieDomain` | string? | `null` | Cookie Domain；空 = 当前 host（推荐同源反代） |
| `Session:Mode` | enum | `Multi` | `Multi` / `Single`（单端挤下线） |
| `Session:MaxConcurrent` | int | `0` | 最大并发会话；`0` = 不限 |
| `Session:ActivityThrottleSeconds` | int | `60` | 闲置跟踪时活动回写 DB 的节流秒数 |
| `Session:IdleMinutesNormal` | int | `0` | 普通用户闲置超时（分）；**`0` = 不启用** |
| `Session:IdleMinutesMfa` | int | `0` | 已开 TOTP 用户闲置（分）；`0` = 与 Normal 相同 |
| `Session:AbsoluteHours` | int | `0` | 绝对会话最长小时；**`0` = 不额外限制**（仅随 refresh） |

### 1.3 既有、保持不变

| 前缀 | 说明 |
|------|------|
| `LoginLock:*` | 失败锁定（默认开：5 次 / 10 分） |
| `Captcha:*` | 图形验证码（默认关） |
| `RateLimit:*` | IP 限流（部署总开关默认开） |
| `SmsOtp:*` | 短信 OTP（与 TOTP **独立**，默认关） |
| `DataProtection:*` | 信封主密钥（TOTP 种子等） |
| `DefaultInitialPassword` | 默认初始口令；`null` = 随机 |

---

## 2. 已移除的键（配了就启动报错）

| 配置键 | 状态 |
|--------|------|
| `Security:Profile` / `Profile=Level3` | **已移除**。配了它 `AddSmartAdmin` 直接抛，错误消息里带迁移对照 |
| `Security:Level3:*` | **已移除**，同上 |

启动即拒而不是静默忽略：`Profile=Level3` 同时打开 TOTP 与 Cookie 会话，忽略它等于把两项安全能力一起关掉，且关掉的那一刻没有任何迹象。

迁移对照：

| 旧 | 新 |
|----|-----|
| `Profile=Level3` | 按需设 `Totp:Enabled`、`Session:CookieMode`，并自配闲置/绝对寿命（不再有内置下限） |
| `Level3:CookieDomain` | `Session:CookieDomain` |
| `Level3:TotpIssuer` | `Totp:Issuer` |
| `Level3:TotpChallengeTtlSeconds` | `Totp:ChallengeTtlSeconds` |
| `Level3:ReauthWindowMinutes` | `Totp:ReauthWindowMinutes` |
| Level3 内置的闲置 30/15、绝对 8h、并发 3/1、口令 12 位等下限 | 全部取消；要哪条就显式配哪条（`IdleMinutesNormal` / `IdleMinutesMfa` / `AbsoluteHours` / `MaxConcurrent`，口令策略在配置中心「安全策略」） |

---

## 3. 示例

### 默认（什么都不配）

与普通后台一致：无 TOTP、body refresh、无限闲置绝对窗。

### 只开二因子

```json
{
  "SmartAdmin": {
    "Security": {
      "Totp": {
        "Enabled": true,
        "RequireForSuperAdmin": true
      },
      "DataProtection": {
        "Key": "<base64-32bytes+>"
      }
    }
  }
}
```

### 只开 Cookie 会话

```json
{
  "SmartAdmin": {
    "Security": {
      "Session": {
        "CookieMode": true
      }
    }
  }
}
```

### 常用加固组合

```json
{
  "SmartAdmin": {
    "Security": {
      "Totp": { "Enabled": true, "RequireForSuperAdmin": true },
      "Session": {
        "CookieMode": true,
        "MaxConcurrent": 3,
        "IdleMinutesNormal": 30,
        "IdleMinutesMfa": 15,
        "AbsoluteHours": 8
      },
      "DataProtection": { "Key": "<base64>", "KeyVersion": 1 }
    }
  }
}
```

---

## 4. 代码读法

优先用 `AdminSecurityOptions` 上的 helper，而不是自己拼配置键：

| Helper | 含义 |
|--------|------|
| `IsTotpFeatureEnabled` | TOTP 能力开？ |
| `IsCookieSessionEnabled` | Cookie 会话开？ |
| `IsSessionIdleEnabled` | 闲置策略开？ |
| `IsSessionAbsoluteEnabled` | 绝对寿命开？ |
| `ResolveCookieDomain()` | Cookie Domain |
| `ResolveTotpIssuer()` / `ResolveTotpChallengeTtlSeconds()` / `ResolveReauthWindowMinutes()` | TOTP 参数 |
| `ResolveIdleMinutes(mfaUser)` | 闲置分钟 |
| `ResolveAbsoluteTimeSpan()` | 绝对寿命；未启用则 `null` |

`Profile=Level3` 的兼容分支已全部删除（决策见 ADR 0006）。
