# Error Codes

The `code` in the envelope is a number, and the text is looked up on the frontend by `msgKey`. Adding an error to your own module means picking a number that collides with nothing, tagging it with a semantic key, and letting the kernel find it — nothing in the kernel needs touching.

How a code turns into a Chinese or English sentence on the frontend belongs to [Response Contract and Error Codes](/frontend/api-contract).

## Segments

`Core/ErrorCode.cs` is the source of truth. New kernel codes take the next number in their segment and never borrow from another.

| Segment | Purpose |
| --- | --- |
| `0` | Success |
| `40000`–`40999` | Authentication and login |
| `41000`–`41999` | Permissions and data scope |
| `42000`–`42999` | User, org, role, menu |
| `43000`–`43999` | Dictionary, config |
| `44000`–`44999` | File upload |
| `45000`–`45999` | Notifications |
| `46000`–`46999` | Import, export |
| `47000`–`47999` | Scheduled jobs |
| `48000`–`48999` | Request parameters |
| `50000`–`50999` | Internal system errors |

Consumer codes take a segment of their own, by convention starting at `60000`, clear of the `40000`–`48999` and `50000`–`50999` the kernel already holds.

## Registering your own codes

Write the enum the way the kernel does: its own segment, every member tagged with `[MsgKey]`.

```csharp
public enum WoErrorCode
{
    [MsgKey("error.wo.notFound")]
    WoNotFound = 60001,

    [MsgKey("error.wo.statusConflict")]
    WoStatusConflict = 60002,
}
```

Throw it by casting to the kernel's `ErrorCode`. `AdminException` accepts only that enum type, and this cast is the interface:

```csharp
AdminException.ThrowIf(wo is null, (ErrorCode)WoErrorCode.WoNotFound);
```

There are two ways in:

- **Automatic scanning of `ApplicationAssemblies`**, which is the everyday path. Business assemblies are registered with the kernel anyway (their entities join table creation, their controllers get mounted), and at composition time every enum in them with `[MsgKey]` members is swept into the catalog. You write no extra line.
- **Explicit registration via `options.ErrorCodeEnums`**, needed only when the codes live in some other assembly that isn't registered as a business assembly.

```csharp
builder.Services.AddSmartAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(WoService).Assembly);   // WoErrorCode is swept in along with it
    o.ErrorCodeEnums.Add(typeof(SharedErrorCode));             // only needed outside business assemblies
});
```

Once registered, an exception thrown as `(ErrorCode)WoErrorCode.WoNotFound` carries `error.wo.notFound` as its envelope `msgKey`. Unregistered, it falls back to `error.code.60001`, a key no locale file has, and that raw string is what pops up in the user's face — registering is what saves a consumer from copying the kernel's whole exception filter and maintaining a code table alongside it, just to get the right `msgKey`.

If two enums claim the same number, registration throws on the spot and names both sides. The frontend looks up text by `code`, so one number mapped to two keys is guaranteed to render one of them wrong; that clash must not wait until runtime to surface.

## `GET /api/v1/meta/error-codes`

An anonymous endpoint returning the full catalog, consumer-registered codes included.

```json
{
  "code": 0,
  "msgKey": "common.success",
  "data": [
    { "code": 0, "name": "Success", "msgKey": "common.success", "source": "ErrorCode" },
    { "code": 40001, "name": "PasswordWrong", "msgKey": "error.auth.passwordWrong", "source": "ErrorCode" },
    { "code": 60001, "name": "WoNotFound", "msgKey": "error.wo.notFound", "source": "WoErrorCode" }
  ]
}
```

`source` says which enum a code came from: `ErrorCode` for the kernel's, your own type name for yours.

It's anonymous because it is a public code table with no business data in it, and the callers that need it (a login page translating an error, an external integrator wiring up, an agent calling by contract) mostly don't hold a token yet. The whole module can be switched off with `Api:DisabledModules=["Meta"]`.

There's no matching `meta/permissions`. The permission-code list already exists at `menu/routes`, and that one is permission-gated; opening an anonymous alias to it would hand "which endpoints does this system have" to reconnaissance for free.

## `ErrorCode` in the contract

`components.schemas.ErrorCode` in `/openapi/v1.json` carries the same table:

```json
{
  "type": "integer",
  "format": "int32",
  "description": "业务错误码。0 = 成功;码表见 x-enum-values / x-enum-varnames / x-msg-keys,或调 GET /api/v1/meta/error-codes(消费者登记的码也在里面)。",
  "x-enum-values": [0, 40001, 60001],
  "x-enum-varnames": ["Success", "PasswordWrong", "WoNotFound"],
  "x-msg-keys": ["common.success", "error.auth.passwordWrong", "error.wo.notFound"]
}
```

The three arrays line up index by index. `x-enum-varnames` is the established openapi-generator convention for producing named enums; `x-msg-keys` is this repo's own addition, mapping a code straight onto a frontend i18n key so nobody has to maintain that table by hand.

`type` stays an open `integer` and **deliberately carries no `enum`**. With one, generators render it as a literal union and a caller passing a plain number fails to compile. Worse, the code space is open to consumers by design, so a closed `enum` would declare every code in the `60000` segment illegal in the contract while those codes fly for real every day.
