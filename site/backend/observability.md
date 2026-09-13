# Tracing and Metrics

The kernel emits trace spans and metrics through one `ActivitySource` and one `Meter`, both named `SmartAdmin`. They're built on .NET's own diagnostic APIs and add no dependency, which is why there's no switch to flip: with nobody listening, `StartActivity` returns null and the whole cost is one null check.

## One name, both outlets

```csharp
public static class SmartAdminDiagnostics
{
    public const string NAME = "SmartAdmin";

    public static readonly ActivitySource Source = new(NAME);
    public static readonly Meter Meter = new(NAME);
}
```

The activity source and the meter share this single name, so subscribing means remembering one string.

## Four counters

| Counter | Tags | What it records |
| --- | --- | --- |
| `smartadmin.authorizations` | `result` = `allow` \| `deny` \| `unauthenticated` \| `session-dead` | Every authorization decision made by `[RolePermission]` |
| `smartadmin.logins` | `result` = `success` \| `failure`; failures also carry `code` | Every login attempt |
| `smartadmin.rate_limited` | none | Requests rejected by the rate limiter |
| `smartadmin.job_runs` | `result` = run status (`Success` / `Failed` / `Timeout`, …) | Every scheduled job run as it closes out |

The tags are where the value of these four sits. The raw total of `smartadmin.authorizations` only tells you how busy the system is, whereas a rising `deny` curve means someone is sweeping your endpoints, and a rising `session-dead` means a batch of users was just kicked or the session lifetime is configured too short. Tagging login failures with `code` separates "wrong password" from "wrong captcha", and credential stuffing does not look like a user fumbling a form.

## The authorization span

Each `[RolePermission]` decision opens a `smartadmin.authorize` span carrying two tags:

| Tag | Value |
| --- | --- |
| `smartadmin.permission_code` | The endpoint's permission code, e.g. `GET:/api/v1/sys/user/page`. Super admins and unauthenticated requests never reach this step, so they carry no such tag |
| `smartadmin.result` | Same value set as the counter's `result` |

With the permission code on the span, "who got denied on which endpoint" becomes one filter in your APM instead of a log trawl matched up by timestamp.

## Wiring it up

For OpenTelemetry, subscribe by name in the host:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(SmartAdminDiagnostics.NAME))
    .WithMetrics(m => m.AddMeter(SmartAdminDiagnostics.NAME));
```

You can also read them without wiring anything. `dotnet-counters` attaches to the live process, no code change and no restart:

```bash
dotnet-counters monitor --process-id <pid> --counters SmartAdmin
```

When there's one machine in front of you and the only question is whether that burst of 403s came from a single caller, this command beats standing up a collection pipeline.

## Exception logs prefer the W3C trace id

`TraceId` in `sys_exception_log` takes the W3C trace id from `Activity.Current` first, falling back to `HttpContext.TraceIdentifier` only when there isn't one:

```csharp
TraceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier,
```

That fallback value looks like `0HN7GK2M8QJ1B:00000003` — a connection id and a sequence number, meaningful inside this process and nowhere else. A W3C trace id is shared by the whole call chain, so searching your APM for it surfaces what the gateway, the frontend and the downstream services were doing either side of the failure. **The column only gains cross-process meaning once tracing is wired up**; without it you get the process-local identifier, never an empty value.

## Health check bodies are JSON

The response body of `/health` and `/health/ready` is JSON. **Status codes follow the usual convention**: Healthy is 200, anything else 503, so orchestrator probes can judge by status code alone; a script that needs the exact state reads the body's `status` field.

```json
{
  "status": "Healthy",
  "totalMs": 12,
  "checks": [
    { "name": "db", "status": "Healthy", "ms": 8, "description": null, "error": null, "tags": ["ready"] },
    { "name": "cache", "status": "Healthy", "ms": 3, "description": null, "error": null, "tags": ["ready"] }
  ]
}
```

`/health` only reports process liveness and runs no dependency checks, so its `checks` is always an empty array. Per-check detail exists on `/health/ready` alone.

A bare status code is enough for a probe and not enough for a person: when ready goes red you can't tell the database from the cache, let alone see which one was slow. So the body lists each check with its state and timing:

```json
{
  "status": "Unhealthy",
  "totalMs": 5031,
  "checks": [
    { "name": "db", "status": "Unhealthy", "ms": 5030, "description": "数据库不可达", "error": "SqlException", "tags": ["ready"] },
    { "name": "cache", "status": "Healthy", "ms": 1, "description": null, "error": null, "tags": ["ready"] }
  ]
}
```

`error` gives the exception's type name and not its message. Health checks are anonymous endpoints, and exception messages routinely carry connection strings, host names and database names.

Which probe maps to which kind of k8s probe, and how a reverse proxy should let them through, is covered in the [deployment guide](/guide/deployment/).
