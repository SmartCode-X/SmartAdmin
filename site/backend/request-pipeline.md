# Request Pipeline

An admin kicks someone out of the Online Users list. That user's JWT hasn't expired, and their very next request comes back 401 anyway. The token has no idea it was revoked — the authorization gate re-checks the session on every request. The whole pipeline divides its labor this way: business code stays unaware, and the behavior is settled once by the kernel, at a fixed spot.

## Overview

```text
HTTP request
  │
  ├─①  Authentication   Microsoft JWT Bearer
  │          Claims are not remapped (sub / sid / sadm / unique_name)
  │          Framework 401 challenge → unified envelope (40006)
  │
  ├─②  [RolePermission]   Authorization filter
  │          Unauthenticated → 401; super admin sadm → pass through
  │          Validates session sid is still active (force-logout takes effect immediately)
  │          Permission code = {METHOD}:/{route template}, matched against the user's permission code set
  │
  ├─③  Data scope   Resolves the effective org set, writes it into IDataScopeContext
  │
  └─④  Result envelope   Bare return dto → Result<T>
             AdminException / ErrorCode → envelope (numeric code, never localized text)
```

## ① Authentication: Microsoft JWT Bearer

The kernel uses `Microsoft.AspNetCore.Authentication.JwtBearer` directly, without building its own auth stack. Wired up in `SmartAdminSetup.cs`:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SymmetricSecurityKey>((o, signingKey) =>
    {
        o.MapInboundClaims = false;   // keep original claim names, no legacy remapping
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Jwt.Issuer,
            IssuerSigningKey = signingKey,
            ValidateAudience = false,          // single monolithic backend, audience not used
            ValidateLifetime = true,           // validate exp / nbf
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
        };
        o.Events = new JwtBearerEvents { OnChallenge = /* see below */ };
    });
```

**Claims are not remapped.** `MapInboundClaims = false` turns off .NET's default legacy behavior of rewriting `sub` into a long XML-namespace URI, so claim names in the token are preserved as-is. The kernel's custom claim names are centralized in `TokenClaimNames` (`Core/Security/ITokenProvider.cs`):

| Claim | Constant | Meaning |
| --- | --- | --- |
| `sub` | `JwtRegisteredClaimNames.Sub` | User primary key |
| `sid` | `TokenClaimNames.SESSION_ID` | Session identifier (force-logout anchor) |
| `sadm` | `TokenClaimNames.SUPER_ADMIN` | Super-admin flag (value `"true"` bypasses authorization directly) |
| `org` | `TokenClaimNames.ORG_ID` | Owning org Id (data-scope anchor) |
| `unique_name` | `JwtRegisteredClaimNames.UniqueName` | Login account, mapped to `User.Identity.Name` |

**Framework 401s are reshaped into the unified envelope.** By default, when a token is missing or expired, JwtBearer returns an empty 401 whose body isn't in the kernel's envelope format. `OnChallenge` intercepts it and rewrites it into the same `Result<T>` shape used by business responses:

```csharp
OnChallenge = async ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
};
```

`ErrorCode.TokenInvalid` corresponds to numeric code **40006**. This way, whether the frontend hits "token expired" or "no permission," it receives an isomorphic envelope it can handle uniformly by code.

::: tip Deny by default
Controller endpoints require authentication by default via `MapControllers().RequireAuthorization()`, respecting `[AllowAnonymous]`. Anonymous endpoints like login and captcha are explicitly opened up; everything else passes through authentication first.
:::

Machine clients get a second scheme, `ApiKey`: an endpoint marked `[ApiKey]` accepts only the pre-shared key in the request header, never a user JWT; a missing or wrong key answers 401 + `40027`. When the key is bound to a user, the `[RolePermission]` check, data scope and operation log that follow all run as that user — no second model. Usage and configuration are in the "API key access" section of "Authentication & security".

## ② `[RolePermission]`: the permission code IS the route

Authorization is handled by `RolePermissionAttribute` (implementing `IAsyncAuthorizationFilter`). It **takes no parameters and no permission strings**. Magic strings like `"sys:user:add"` never appear in code. Authorization is granted by checking routes in the role-menu UI.

The filter runs in a fixed order internally:

```csharp
// 1. Must have passed JWT authentication
if (user.Identity?.IsAuthenticated != true)
    → 401 + 40006

// 2. Session-activity check: the session behind sid was revoked/expired → 401 (applies to super admins too)
var sessionId = user.FindFirstValue(TokenClaimNames.SESSION_ID);
if (!await sessions.IsActiveAsync(sessionId))
    → 401 + 40006

// 3. Super admin passes through directly + unrestricted data scope
if (user.HasClaim(TokenClaimNames.SUPER_ADMIN, "true"))
{
    scopeContext.Current = DataScopeResult.Unrestricted;
    return;
}

// 4. Regular user: resolve data scope and write it into the context (see ③)
// 5. Permission code match
var code = PermissionCode.Build(method, routeTemplate);
if (!codes.Contains(code)) → 403 + 41001
```

**Permission code = normalized route.** `PermissionCode.Build` is the single source of truth:

```csharp
public static string Build(string httpMethod, string? routeTemplate) =>
    $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";
// e.g.: GET:/api/v1/ping
```

Using the **route template** rather than the actual path means parameterized routes (`user/{id}`) get a stable permission code that doesn't vary with the parameter value. The same `Build` function is shared across three call sites — authorization matching, `MenuController.Routes`' route listing (which feeds the permission-code dropdown on the menu form), and the default operation name for operation logging — preventing "the code computed at authorization time" from silently drifting one character out of sync with "the code stored on the menu" due to case or slash differences.

### One button, several routes

The unit of granting is a menu button, not a route. A button's `Permission` field may carry several codes joined by `;`; checking that button for a role grants all of them. Every page in the kernel seed has the same four buttons: Query, Create, Update, Delete. Query covers the list and detail routes, Delete folds single and batch delete together, and the four steps of an import are one Import button. Only operations specific to a page (reset password, enable/disable, run once) get a button of their own.

Splitting and joining live in one place, `PermissionCode.Split` and `PermissionCode.Join`: split on `;`, trim, normalize each code, dedupe. `RbacPermissionProvider` splits before taking the union of a user's codes, and `MenuService` splits and re-joins before saving a button, so `get:/API/v1/Ping` never reaches the database. Authorization itself always works on a single route: `[RolePermission]` computes the code for the incoming route and looks it up in the user's set; a checkbox merely expands into several routes. `v-auth` on the frontend keeps taking a single route code as well.

**Session-activity checks make force-logout take effect immediately.** Step 2 calls `ISessionService.IsActiveAsync(sid)` on every request. When an admin kicks a user from "Online Users," that session's cache entry is removed and the DB row is marked revoked — the kicked user's access token, even if not yet expired, gets a 401 on the very next request. Super admins aren't exempt either.

::: tip `[ActiveSession]`: endpoints for any logged-in user
Endpoints like personal center or logout — usable by any logged-in user without a specific permission code — carry `ActiveSessionAttribute` instead. It performs only steps 1 and 2 above (authentication + session-activity check), skipping the permission-code match. Using `[Authorize]` alone without it means an unexpired token still works even after the session was force-revoked — so any endpoint that needs force-logout to take effect immediately must carry `[ActiveSession]`.
:::

## ③ Data scope: resolved and injected into `IDataScopeContext`

During authorization (steps 3 and 4), the current user's **effective data scope** is resolved as a side effect and written into `IDataScopeContext`:

```csharp
// Super admin
scopeContext.Current = DataScopeResult.Unrestricted;

// Regular user (cache-backed)
var userId = long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
scopeContext.Current = await dataScopeProvider.ResolveAsync(userId, abort);
```

This is written during authorization (before the action runs) because DB queries inside the action need it. Once written, subsequent queries against `DataEntity` within the same request are automatically filtered by org set through SqlSugar's global query filter — business code never writes a single filter condition. See [Multi-Org Data Scope](./data-scope.md) for the full mechanism.

## ④ Result envelope: even bare returns get wrapped

At the response stage, the kernel uniformly wraps output into the `Result<T>` envelope and converts business errors into it too.

**Success: bare `return dto` gets auto-wrapped.** `ResultEnvelopeFilter` (an `IAsyncResultFilter`) lets business controllers `return dto;` directly and still get a unified envelope, without hand-writing `Result.Ok(...)` everywhere:

```csharp
public static bool TryWrap(IActionResult result, out ObjectResult wrapped)
{
    wrapped = null!;
    if (result is not ObjectResult obj) return false;        // File/StatusCode etc. left untouched
    if (obj.Value is IResultEnvelope) return false;          // already an envelope, pass through
    if (obj.StatusCode is int sc && (sc < 200 || sc >= 300)) return false; // non-2xx not wrapped
    wrapped = new ObjectResult(Result<object?>.Ok(obj.Value)) { StatusCode = obj.StatusCode };
    return true;
}
```

Only **successful (2xx) bare `ObjectResult`s** get wrapped; File, StatusCode, and error results are left untouched. Built-in controllers still explicitly return `Result<T>` (to keep the OpenAPI contract accurate) — this filter is a no-op for them.

::: tip The contract wraps too
The filter wraps the envelope at result-execution time, which is invisible to ApiExplorer. Contract generation applies the same rule (a declared type that isn't an envelope gets wrapped) and adds the envelope shell to the 200 schema: a consumer endpoint that bare-returns `dto` gets the same wrapped shape recorded in the contract, so a fresh `gen:api` produces correct frontend types — without this, the generated types would treat `data` as a top-level field, raising no error while simply coming back empty. Built-in controllers all return `Result<T>` explicitly and are unaffected.
:::

**Failure: `AdminException` → envelope.** Expected business failures (wrong credentials, wrong captcha, no permission…) throw `AdminException`, converted by `AdminExceptionFilter` into an HTTP 200 with a business-code envelope:

```csharp
public void OnException(ExceptionContext context)
{
    if (context.Exception is not AdminException ex) return;
    if (ex.InnerException is null)
        logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    else
        logger.LogWarning(ex.InnerException, "业务失败 {Code}({MsgKey}):{Path}", (int)ex.Code, ex.MsgKey, ...);
    context.Result = new ObjectResult(Result<object>.From(ex));
    context.ExceptionHandled = true;
}
```

Business failures are logged at **Information** level (not an error, doesn't trigger alerting). The ones carrying an `InnerException` are logged at **Warning** instead, one notch louder than an ordinary business failure, because that shape usually means a wrapped external-call failure — a full disk, an expired upstream credential. Other exceptions are not intercepted here — they fall through to the framework's default 500 handling, preserving the full stack trace, because a genuine program defect should fail loudly.

**Errors are numeric codes, never localized text.** The envelope carries `{ code, msgKey, args, message }`, where `code` is the numeric value of the `ErrorCode` enum. i18n happens on the frontend, keyed by code — the backend never returns Chinese/English error copy.

## An oversized page errors instead of being truncated

A page size above `SmartAdmin:Api:MaxPageSize` (default 200) throws `48001 PageSizeExceeded`.

```json
{
  "SmartAdmin": {
    "Api": { "MaxPageSize": 200 }
  }
}
```

Without this check, an oversized request would quietly query at the cap and return success, handing the caller a result missing most of its rows with nothing to show for it. Anywhere paging is used as an export takes that blow hardest: you think you got everything, and you got the first 200 rows. Raise `MaxPageSize` if you need bigger pages; for a full dump use an explicit export instead of routing it through paging.

## Four extension fields in the contract

Every operation in `/openapi/v1.json` carries four things, all of them meant for machines:

| Field | Value |
| --- | --- |
| `operationId` | `{Controller}_{Action}`, with a numbered suffix on collisions. Without it, action names alone would collide into ten `Delete`s and eight `Update`s distinguished only by path, leaving SDK generators and LLM tool calls to guess |
| `x-permission-code` | The endpoint's permission code, e.g. `GET:/api/v1/sys/user/page`. Present only where `[RolePermission]` is applied |
| `x-auth` | `anonymous` \| `apikey` \| `permission` \| `session` \| `authenticated` |
| `x-module` | The owning module — the name you'd use to switch it off wholesale via `Api:DisabledModules` |

`x-permission-code` shares its source with the authorization decision: both go through `PermissionCode.Build`, so the contract and the role-authorization screen can't disagree.

The error-code catalog ships in the same contract, covered in [Error Codes](./error-codes.md).

## Walking through one call

Take "delete a role" as an example:

1. **Authentication** — validates the JWT signature and expiry, reads out `sub` / `sid` / `sadm`.
2. **`[RolePermission]`** — session `sid` is still active; not a super admin; permission code `DELETE:/api/v1/sys/role/{id}` is present in the user's permission code set → pass through. The user's data scope is also written into `IDataScopeContext` at this point.
3. **Data scope** — the repository's write-path guard first queries through the scope-filtered path to confirm the target row is within scope; attempting to modify/delete a row from another org returns 0 rows and is rejected.
4. **Result envelope** — the controller's `return` result is wrapped into `Result<T>`; if an `AdminException` was thrown along the way, it's converted into a business-code envelope instead.
