# External Login (SSO)

The first time someone scans in through WeCom, they get rejected with `OAuthAccountNotBound`. That's the default policy, not a misconfiguration. The kernel's standing position is that accounts are provisioned by an admin, never self-registered — and external identity follows the same rule. To let SSO provision accounts on its own, the switch has to be turned on explicitly, per provider.

## Five providers, two packaging models

| Provider | Where it lives | How it connects |
| --- | --- | --- |
| `oidc` | Built into the kernel (AspNetCore layer) | Standard OIDC — works with Keycloak, Entra, Authing, Auth0, and anything else that speaks the protocol |
| `wecom` | Optional package `SmartAdmin.Auth.WeCom` | QR login in a desktop browser, web authorization inside the WeCom client, picked automatically by User-Agent |
| `dingtalk` | Optional package `SmartAdmin.Auth.DingTalk` | DingTalk desktop QR / web authorization |
| `github` | Optional package `SmartAdmin.Auth.GitHub` | GitHub OAuth App, scope limited to `read:user` |
| `wechat` | Optional package `SmartAdmin.Auth.WeChat` | WeChat Open Platform website-app QR login (`snsapi_login`), identity resolved from `unionid` alone |

Built-in OIDC adds zero new dependencies: discovery document, JWKS, and `id_token` signature verification all ride on the `Microsoft.IdentityModel.*` stack that JwtBearer already pulls in. Each vendor package depends only on `Core` plus Microsoft.\*, talking to the vendor's API over a bare `HttpClient`. That's what lets them ship on their own release cadence without dragging a vendor SDK into the kernel.

The four optional packages follow the usual rule — register before `AddSmartAdmin()`. They coexist with the built-in provider, keyed by `Code`:

```csharp
builder.Services.AddSmartAdminWeComAuth(builder.Configuration);
builder.Services.AddSmartAdminDingTalkAuth(builder.Configuration);
builder.Services.AddSmartAdminGitHubAuth(builder.Configuration);
builder.Services.AddSmartAdminWeChatAuth(builder.Configuration);
builder.Services.AddSmartAdmin(builder.Configuration);
```

## Configuration lives in two places

Secrets and operational settings are deliberately kept apart.

**Connection details and secrets go in `appsettings`**, the same pattern as Database, Jwt, and Email — secrets never land in the database:

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

`CallbackBaseUrl` takes only the backend's public root address; the kernel appends the callback path `/api/v1/auth/external/{provider}/callback` itself. Leave it unset in development and it falls back to the request host; in production it's required, because the Host header can be forged. Get it wrong and the vendor still redirects — vendors only check the domain — so the user lands on a path that doesn't exist, and it looks as if authorization finished and nothing happened. That's why it's validated at startup: a full callback URL, the frontend result page (`FrontendResultPath`), a query string or `#` fragment, or anything that isn't an absolute `http(s)` URL makes the app refuse to start. A gateway sub-path is fine, e.g. `https://gw.example.com/admin`.

`GET /api/v1/auth/external/providers` only returns the non-secret fields (code, display name, icon) — just enough for the frontend to light up a button.

**Operational settings go through `sys_config`**, changeable at runtime from the config page, keyed by provider code:

| Config key | Default | Controls |
| --- | --- | --- |
| `sys.externalauth.{code}.enabled` | Enabled | Whether this provider is turned on |
| `sys.externalauth.{code}.provisioning` | Deny | Whether an unbound identity's first login provisions a new account |
| `sys.externalauth.{code}.linkByAccount` | Off | Whether an unbound identity is linked to the local account with the same name |
| `sys.externalauth.{code}.defaultRoleIds` | Empty | Roles granted on auto-provisioning |
| `sys.externalauth.{code}.defaultOrgId` | Empty | Org assigned on auto-provisioning |

Leave every key unset and the default behavior is **enabled + deny provisioning**. Touch only `appsettings` and you already have a working, binding-first SSO setup.

`enabled` is toggled on the cards of the config center's External login tab, and the WeCom card has one more switch, "Link by account", for its `linkByAccount`. None of the other keys are seeded, and that includes `linkByAccount` for any other provider. Add them from the Other tab when you need them, with the group set to `externalauth`; after saving they're listed at the bottom of the External login tab under "Other configs in this group".

Reading these keys is consolidated in `ISysUserExternalService` — both the controller and `AuthService` call through it rather than each reading config keys on their own.

::: tip No provider-admin page, on purpose
The backend doesn't create a provider table or an admin page for it. Vendor secrets are deployment infrastructure at heart — storing them in the database would mean encryption at rest, masking, and a whole CRUD surface for a marginal gain that doesn't justify the added attack surface or the work. If a standalone provider-admin page is ever genuinely needed, the frontend can layer one on without any backend changes.
:::

## What happens to an unbound identity

The `sys_user_external` table is unique on `(Provider, Subject)` — it records which external identity maps to which local user. When a first external login finds no binding, a provider with `linkByAccount` on first tries to link by account name; if it's off or finds no match, the `provisioning` switch decides what happens next:

- **Deny** (default): internally throws `OAuthAccountNotBound` (40016), but the login callback never hands that error straight to the user — it converts into a one-time "pending-link" ticket instead, steering the user toward a password or SMS login; once that succeeds, `POST pending-link/claim` with the ticket is what actually binds the external identity to the account (claiming requires the same browser that started the login, to block phishing-based hijacking). The other direction works too: get a local account first, then start a binding from the personal-center page with `POST {provider}/bind`.
- **Auto-provision (JIT)**: creates a local account with a random placeholder password and no forced password change, assigning the role and org from the two config keys above.

### Linking by account name

When the local accounts already are the WeCom accounts, the pending-link claim is a wasted step: employees have no password to type in. Set `sys.externalauth.{code}.linkByAccount` to `true`, and when the external identifier (the `userid`, for WeCom) is identical to a local account name, the first login binds the two and signs the person straight in. From then on only the binding counts; names are never matched again. The comparison is the same one password login uses to look up an account, so whether case matters comes down to the database collation. WeCom `userid`s are case-insensitive themselves, so keep the spelling consistent on both sides. Switching it on from the WeCom card asks for confirmation first; for other providers, add the key from the Other tab as described above.

Super-admin accounts, disabled accounts, and accounts already bound for this provider are never linked automatically, and neither is an identity with no same-name account; all of them fall through to `provisioning`. A super admin who wants external login has to bind it from the personal center. With auto-provisioning on as well, linking is tried first and an account is provisioned only when linking comes up empty. To link on some other attribute, such as email or phone, override `AuthService.LinkByAccountAsync` or `ResolveExternalUserAsync` — no kernel changes needed.

::: warning Make sure both sides are the same people before turning it on
With it on, "who is this person" is answered by the IdP's accounts.

- Both account sets must come from the same source and belong to the same people, e.g. local accounts synced from the WeCom directory. Where each side creates accounts on its own, the same name doesn't mean the same person, and a link puts someone into a colleague's account. That's the most realistic risk; compare the two lists before enabling.
- Whoever can rename an account in the IdP can make someone sign in as the local account of that name. In WeCom, only admins and the directory-sync API can change a member's account; ordinary members can't change their own. Inside a company, that permission usually sits with IT.
- When someone's WeCom account is renamed, follow up locally by renaming the local account or unbinding and rebinding. Otherwise the renamed account's next first login goes looking for a different local account with that name.
- Only turn it on for providers whose identifiers the organization assigns and users can't pick. WeCom `userid` fits: non-members only get an openid, and members of interconnected enterprises arrive as `CorpId/userid`, so neither matches an ordinary account. DingTalk, WeChat and GitHub identifiers are unionids or numeric ids that won't match anyway. An OIDC IdP that allows self-registration, with `sub` being the username, must not have it on.
:::

## Signing in from inside the WeCom client

The one `wecom` provider serves two authorization pages, chosen by the User-Agent of the request that starts authorization. A UA containing `wxwork` is the WeCom client's built-in browser, desktop and mobile alike. The QR page is useless there, so it goes through OAuth web authorization instead, with `scope` set to `snsapi_base`: silent, no confirmation page. Everything else gets the QR login. The `code` from either page is exchanged for a `userid` through `auth/getuserinfo`, and the rest of the flow is identical.

For employees to land in the system by opening the app inside WeCom, configure two things in the WeCom admin console:

- Point the app's homepage at the site.
- Make the trusted domain cover `CallbackBaseUrl`'s domain, registering the port too if there is one; otherwise the redirect fails with a `redirect_uri` parameter error.

Inside the WeCom client, the frontend package's built-in login page starts a WeCom login on its own, once per browser session. After a failed login, or after signing out and returning to the login page, it stays put and lets the person choose — no bouncing back and forth.

The first login still has to get past the unbound check. With the default deny, the employee lands back on the login page to go through the pending-link claim. If the local accounts are the WeCom accounts, turn on "Link by account" on the WeCom card of the External login tab. Employees then get straight in the first time they open the app, with no password claim. If they have no local account yet, turn on auto-provisioning.

## Endpoints

All mounted under `api/v1/auth/external`:

| Endpoint | Purpose |
| --- | --- |
| `GET providers` | List available providers, for the frontend to render login buttons |
| `GET providers/all` | Admin-side: list every registered provider (disabled ones included) with its enabled and link-by-account switches, for the config page's provider cards |
| `GET {provider}/authorize` | Get the redirect URL, carrying a one-time state |
| `GET {provider}/callback` | Where the vendor calls back |
| `POST exchange` | Exchange a one-time ticket for a token |
| `POST pending-link/claim` | Claim an unbound external identity: after a successful password/SMS login, bind the identity resolved during the callback to the current user |
| `GET bindings` | The current user's bound external identities |
| `POST {provider}/bind` | Personal center: start binding an external identity |
| `DELETE {provider}/binding` | Personal center: unbind an external identity |

Both `state` and the one-time ticket reuse the same pattern as SMS verification codes: stored in the cache, consumed via an atomic `GetAndRemoveAsync`, valid exactly once. `state` uses only letters and digits, the only characters WeCom's and WeChat's OAuth accept.

Once external login resolves a `SysUser`, it hands off to `AuthService.CreateTokenAsync` — the same tail end used by password login and SMS login: session creation, token issuance, all of it shared. So session concurrency policy, force-logout, and refresh-token rotation apply to it identically.

## Callback and token-exchange example

`authorize` and `callback` are both full-page browser redirects, not JSON endpoints a frontend can `fetch`. `authorize` sends a 302 to the IdP's authorization page; once the IdP has authenticated the user, it redirects back to `callback`, which 302s again to the frontend's result page (`FrontendResultPath`, `/oauth/callback` by default) with whatever the next step needs, in the query string:

```
Success: GET /oauth/callback?ticket=<one-time ticket>
Failure: GET /oauth/callback?error=40015
```

The frontend reads `ticket` off that result page and exchanges it for a token — this is the step that's actually a `fetch`-able endpoint:

```bash
curl -X POST http://localhost:5100/api/v1/auth/external/exchange \
  -H "Content-Type: application/json" \
  -d '{"ticket":"<ticket from the callback redirect>"}'
```

The response envelope has the same shape as [password login](/guide/getting-started):

```json
{ "code": 0, "data": { "accessToken": "eyJ...", "expiresAt": "...", "refreshToken": "...", "mustChangePassword": false } }
```

The ticket is single-use — `exchange` consumes it via an atomic `GetAndRemoveAsync`, and a second exchange attempt gets `OAuthStateInvalid` (40014).

## Error codes

| Code | Name | When |
| --- | --- | --- |
| 40013 | `OAuthProviderDisabled` | This provider has been switched off at runtime |
| 40014 | `OAuthStateInvalid` | The state doesn't match, or has already been consumed |
| 40015 | `OAuthExchangeFailed` | The token exchange with the vendor failed |
| 40016 | `OAuthAccountNotBound` | No binding exists, and this provider doesn't allow auto-provisioning |
| 40017 | `OAuthAlreadyBound` | This external identity is already bound to a different account |

Per the [frontend contract](/frontend/api-contract) convention, each of these codes needs a matching `msgKey` string configured in both language packs. Miss one, and the backend's consistency test turns red.
