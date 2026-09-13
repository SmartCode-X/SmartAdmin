# Ops Endpoints

Once the system's running, an admin keeps needing to answer three questions: is this machine still healthy, what was that 500 a minute ago, and does the cache need clearing. The kernel gives each question its own small endpoint. What they share is that they're **read-only diagnostics or a single targeted action, not business features** — so all three can be switched off as a group via `Api.DisabledModules` without touching anything else.

## Server monitor

`GET /api/v1/sys/monitor/server` returns a one-shot snapshot of the process and host: CPU, memory, disk, runtime info. All of it comes straight from the BCL (`Process`/`GC`/`DriveInfo`/`RuntimeInformation`) — zero dependencies, nothing written to the database. It's a **snapshot**, not a time series; historical trends are out of scope for the kernel.

```csharp
public class MonitorService(TimeProvider time, ILogger<MonitorService> logger) : IMonitorService
{
    protected virtual TimeSpan CpuSampleWindow => TimeSpan.FromMilliseconds(500);

    public virtual async Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        // ...MachineName / OsDescription / ProcessorCount read directly
        ProcessCpuPercent = await SampleCpuPercentAsync(proc, cancellationToken),
        // ...
    }
}
```

CPU usage isn't a number the OS hands over directly — it's **computed from a sample**. Take `Process.TotalProcessorTime` once, wait 500 ms, take it again, then divide the delta by elapsed wall-clock time and core count to get a 0–100 percentage. That 500 ms window is `CpuSampleWindow`: a longer window smooths the number but drags out every request, and 500 ms is the tradeoff a manual-refresh click can tolerate.

The built-in frontend page (`web/packages/admin/src/views/system/monitor`) has a single manual-refresh button and **doesn't poll**, for exactly the reason above: polling would mean the backend samples CPU every 500 ms continuously, and a tool for watching CPU usage that eats its own slice of CPU isn't worth it. For actual continuous monitoring, wire up an external observability stack — Prometheus/Grafana or similar — that's the right tool for that job. This endpoint's only job is "admin clicks once, sees the current state."

When the numbers look off, this endpoint won't page anyone — there's no threshold logic behind it. If CPU or memory looks high, the next step is `docker compose logs app` (or whatever your deployment's log sink is) to find which request is the culprit. This endpoint only gives you the first glance that decides whether it's worth digging further.

## Exception log

`sys_exception_log` is the third log table, sharing a controller (`SysLogController`, `GET /api/v1/sys/log/exception/page` + `DELETE /api/v1/sys/log/exception`) with the operation log and login log — but it's written differently. The first two are recorded deliberately by business code; this one is caught automatically by the global exception filter `ExceptionLogFilter` when an **unhandled exception** bubbles up.

```csharp
internal sealed class ExceptionLogFilter(ILogService logService) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is AdminException) return;   // business exceptions never enter this table

        await logService.RecordExceptionAsync(new ExceptionLogEntry { /* method/path/trace id/type/message/stack */ });
        // deliberately doesn't set ExceptionHandled — the exception still bubbles to the framework's 500, response and stack behavior unchanged
    }
}
```

Two deliberate boundaries that are easy to miss reading the code:

- **`AdminException` is explicitly skipped.** A business exception is an expected branch — its envelope already carries an `ErrorCode`; it's not a defect. Letting it into the exception table would just drown real crashes in noise.
- **The filter never swallows the exception.** It doesn't set `ExceptionHandled` — the request still 500s, the stack still bubbles the same way it always did. This layer only leaves a trace on the side; it doesn't change the existing exception-handling flow. Writing the log is itself best-effort: `RecordExceptionAsync` swallows its own internal failures, so a logging failure on the way to handling a crash can never mask the original exception.

Persisted fields are length-truncated — 2000 characters for the message, 8000 for the stack trace — and **only the exception itself is recorded, never the request or response body** (a response body can carry a plaintext password or token, a line the kernel guards just as hard elsewhere, same as the login log). Clearing is a hard delete, unrecoverable, and the action itself gets written to the operation log — who cleared the exception table and when leaves its own trace.

When triaging one exception record, start with its trace ID (`TraceId`, i.e. `HttpContext.TraceIdentifier`). It's shared with that same request's application logs, so searching it there strings together everything the front and back end actually did around the crash, instead of guessing from the stack trace alone.

## Cache management

All four `CacheController` endpoints **clear**, they don't **inspect**: flush every user's permission/data-scope cache, flush the dict cache, flush the config cache, and bump the portal-menu generation (old cache entries stop being read and get reclaimed by TTL). There's no key-browsing endpoint and no value-reading endpoint — that's a deliberate gap, not something left unfinished:

- The default `MemoryCacheProvider` wraps `IMemoryCache`, which has no supported way to enumerate keys in the first place — key browsing would always be empty on a zero-config deployment anyway.
- Cache keys and values are both off-limits to touch. Keys embed PII like phone numbers and IPs; values can be plaintext verification codes or one-time tokens. Listing keys is already a leak, and reading values would hand an admin a backdoor around the normal flow for viewing an OTP.

```csharp
public virtual Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default) =>
    // bump the generation counter — old portal:* keys stop being read and get reclaimed by TTL, same mechanism RbacService uses on authorization changes
    cache.IncrementAsync(CacheKeys.PortalGeneration, cancellationToken: cancellationToken);
```

None of these four actions come up in normal operation — a real authorization change or dict/config edit already invalidates the matching cache on its own. This page is an escape hatch for **out-of-band** scenarios: someone edited the database directly, bypassing the API, and now the cache and the database disagree — that's when an admin needs to clear it by hand. Every button on the frontend requires a second confirmation, and a toast reports how many entries got cleared afterward. It's built for "infrequent, deliberate action," not a daily-driver control panel.

If you actually get a "the database changed but the page didn't" report, it's most likely the out-of-band scenario described above: a write that went through the API already invalidated the matching cache on its own, so these four buttons aren't the fix for that. Click the clear button matching the data type in question — the symptom usually disappears on the spot. If it's still there afterward, the problem isn't the cache; go back to the database or the application logs.

When the out-of-band write is your own code — a migration script, a sync job — converge in code instead of waiting for an admin to click. `IConfigService.InvalidateAsync(key)` invalidates one config key along with the site info built from it; `IDictService.InvalidateAsync(typeCode)` invalidates one dictionary type. They broadcast `ConfigChangedEvent` and `DictChangedEvent` respectively, the same path a change through the service takes.

Skip the call and the cache still heals, just on expiry: config and dictionary entries share `Cache:PermissionMinutes` with permission codes, 20 minutes by default. For rows ops edits by hand and that must always be read fresh, such as a sync watermark, don't read through the cache at all — query the database with `IRepository<SysConfig>`. The config-cache action on the cache-management page clears every key in one go, site info included.

## Four defaults worth a look before launch

The three endpoints above are for after something goes wrong. This section is the opposite: four settings worth reading before it does. All of them have defaults that run fine, and none of those defaults is necessarily the one you want.

### Signed direct-link lifetime

`Upload:SignedUrlTtlMinutes`, **defaulting to 0, meaning no expiry**. Give it a lifetime and the expiry instant is baked into the signature, so editing `exp` on the URL fails validation.

No lifetime by default, because direct links end up stored inside durable content such as announcement bodies, and giving them a lifetime means they all break together on the day they expire. Configure it only when direct links are confined to avatars, temporary previews, and other things that never land in a body of text.

::: warning Setting it invalidates existing links immediately
Avatar URLs already stored in the database included. Avatars get re-signed the next time they're saved; images inside content bodies you have to migrate yourself.
:::

### How operation logs reach the database

`Logging:OpLog:Sync`, defaulting to `false`, i.e. asynchronous. The request thread only drops a fully populated row into a bounded queue, and a background worker inserts in batches (200 rows per batch by default, with 5 seconds to drain on shutdown). Set `Sync=true` and it falls back to synchronous: that INSERT sits in front of the response, so every write request pays for an extra database round trip.

- `QueueCapacity`, default 10000. When it's full, the oldest is dropped: in an audit trail the newest matters more than the oldest, and blocking a request thread to wait on a log line is the worse option. Approaching the cap raises a warning rather than swallowing the loss.
- `ParamMaxChars`, default 8192. Oversized input keeps its opening and is annotated with the original length, and the **result is still valid JSON** — cut down the middle, the log detail page couldn't parse it. Leave this field unbounded and a five-thousand-row import submission lands its entire payload in the log table verbatim.

For deployments that need "if the log fails, the request fails", or that can never drop a row, `Sync=true` switches to synchronous writes.

### In-process cache limits

`Cache:MemoryEntryLimit` defaults to 100,000 entries (`0` = unlimited) and `Cache:MaxEntryMinutes` to 1440 minutes (`0` = no backstop). The kernel uses its own `MemoryCache`, isolated from the host's `AddMemoryCache()`, so these two govern only what the kernel writes; whatever a consumer caches is untouched.

The expiry backstop is the floor under a configuration like `PermissionMinutes=0`. Without it, permission, scope, dictionary and config keys genuinely never expire, and once somebody edits the database behind the services, that cached copy stays stale forever. Counters are exempt from the backstop and can't be squeezed out by size-based compaction: a portal generation number that expires to zero would fall back a generation, and an evicted rate-limit counter quietly opens the gate.

Once you move to Redis, neither setting means anything; eviction is Redis's own policy from then on.

### PBKDF2 iteration count

`Security:Password:Pbkdf2Iterations`, default 600,000, and **anything below 100,000 is treated as 100,000**. One computation takes tens to a hundred milliseconds, and that cost is deliberate: a brute-forcer pays it per attempt. But it is equally your own CPU bill, since what a hammered login endpoint amplifies is local load — weak hardware should be able to dial it down, strong hardware up.

Changing it doesn't disturb existing rows. The hash string records the iteration count it was made with, so old passwords still verify and are silently recomputed with the new parameter at the user's next login (the plaintext is in hand only at that instant; miss it and you're waiting for the user to change their password). `IPasswordHasher` gains `NeedsRehash` accordingly, with a default interface implementation returning `false`, so third-party implementations are unaffected.

::: tip The kernel has no field-level change log (a DiffLog)
The operation log already records every write request's full input JSON, who did it, when, and the result code. The audit columns cover every row — `CreateUserId`/`UpdateUserId`/`UpdateTime`. Soft-deleted rows are still there to query. Field-level "what was it before, what is it after" was evaluated and deliberately left out of the core. The reason is the write path: `IRepository<>` has no single choke point for writes — Insert/Update/Delete each call SqlSugar directly, so adding a hook would mean adding it to all six methods. And this kind of before-image auditing is inherently a need-it-or-you-don't feature — a consumer who wants it can wire up SqlSugar's own `Aop.OnDiffLogEvent` directly; the kernel doesn't need to do it for them.
:::
