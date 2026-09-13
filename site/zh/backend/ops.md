# 运维端点

系统跑起来之后，管理员总要回答三个问题：这台机器还好吗、刚才那个 500 是什么、缓存是不是该清了。内核给这三个问题各配了一个小端点。它们的共同点是**只读诊断或定向动作，不是业务功能**。所以都能整体经 `Api.DisabledModules` 关掉，不影响其余模块。

## 服务器监控

`GET /api/v1/sys/monitor/server` 返回一次进程与主机的运行快照：CPU 占用、内存、磁盘、运行时信息。全部读自 BCL（`Process`/`GC`/`DriveInfo`/`RuntimeInformation`），零依赖，也不落库。这是一个**快照**，不是一条时间序列，历史趋势不在内核范围内。

```csharp
public class MonitorService(TimeProvider time, ILogger<MonitorService> logger) : IMonitorService
{
    protected virtual TimeSpan CpuSampleWindow => TimeSpan.FromMilliseconds(500);

    public virtual async Task<ServerInfoOutput> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        // ...MachineName / OsDescription / ProcessorCount 直接读
        ProcessCpuPercent = await SampleCpuPercentAsync(proc, cancellationToken),
        // ...
    }
}
```

CPU 占用不是系统 API 直接给的数字，是**采样算出来的**。先取一次 `Process.TotalProcessorTime`，等 500 毫秒，再取一次。用两次的差值除以经过的墙钟时间和核数，换算成 0–100 的百分比。那 500 毫秒的窗口就是 `CpuSampleWindow`。窗口越长，数字越平滑，但每次请求也拖得越久。500 毫秒是页面点一下刷新能接受的折中。

前端页面只有一个手动刷新按钮，**不做轮询**，内置页在 `web/packages/admin/src/views/system/monitor`。原因就在上面那次采样。轮询等于让后端持续每 500 毫秒抽一次 CPU。用来监控 CPU 占用的手段，自己先占了一份 CPU，得不偿失。真要连续监控，接一套外部可观测栈（Prometheus/Grafana 之类）才是对的工具。这个端点只负责「管理员点一下，看一眼当前状态」。

数字异常时，这个端点不会替你报警，它没有阈值判断。看到 CPU 或内存偏高，下一步是去 `docker compose logs app`（或部署环境对应的日志出口）里找是哪个请求在拖，这个端点只负责给你第一眼判断值不值得往下查。

## 异常日志

`sys_exception_log` 是第三种日志表，和操作日志、登录日志共用同一个控制器 `SysLogController`（`GET /api/v1/sys/log/exception/page` + `DELETE /api/v1/sys/log/exception`）。但它的写入方式不一样。前两种由业务代码主动记，这一种由全局异常过滤器 `ExceptionLogFilter` 自动接住**未捕获异常**再写。

```csharp
internal sealed class ExceptionLogFilter(ILogService logService) : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is AdminException) return;   // 业务异常不进异常表

        await logService.RecordExceptionAsync(new ExceptionLogEntry { /* 方法/路径/追踪号/类型/消息/堆栈 */ });
        // 刻意不设 ExceptionHandled —— 异常继续冒泡走框架 500,响应与堆栈行为不变
    }
}
```

两个刻意的边界，读代码时容易忽略：

- **`AdminException` 被显式跳过**。业务异常是可预期的分支，信封里已经带了 `ErrorCode`，不是程序缺陷。让它混进异常表，只会把真正的崩溃淹没在噪音里。
- **过滤器不吞异常**。它不设置 `ExceptionHandled`，请求该 500 还是 500，堆栈该往上抛还是往上抛。这一层只是「旁路留一条痕迹」，不改变原有的异常处理流程。写日志本身也是尽力而为。`RecordExceptionAsync` 会把自己内部的异常吞掉。处理一次崩溃的路上，落日志再失败，也不能反过来把原始异常盖掉。

落库的字段做了长度截断，消息 2000 字符，堆栈 8000 字符。而且**只记异常本身，绝不记请求/响应体**。响应体里可能有明文口令或令牌，这条线内核在别处也反复守，登录日志同理。清空操作是硬删，不可恢复。动作本身还会被记进操作日志。谁在什么时候清了异常表，这件事也留痕。

排查一条异常记录时，先看它的追踪号（`TraceId`，即 `HttpContext.TraceIdentifier`）。这个号和同一请求的应用日志共用一份，拿它去日志里搜，能把「这次崩溃发生前后端到底做了什么」串成一条完整链路，而不是只盯着这一条记录里的堆栈猜。

## 缓存管理

`CacheController` 的四个端点都是「清」，不是「看」：清全部用户的权限/数据范围缓存、清字典缓存、清配置缓存、把门户菜单代际加一（旧缓存不再被读到，靠 TTL 自然回收）。没有键浏览端点，也没有取值端点。这是设计上刻意留白，不是漏做：

- 默认的 `MemoryCacheProvider` 包着 `IMemoryCache`，它本来就没有受支持的键枚举方式。所以键浏览在零配置部署上永远是空的。
- 缓存的键和值都碰不得。键里嵌着手机号、IP 这类 PII，值里可能是明文验证码或一次性令牌。列出键已经算泄露，读值等于给管理员开了一个绕过正常流程看 OTP 的后门。

```csharp
public virtual Task<long> RebuildPortalAsync(CancellationToken cancellationToken = default) =>
    // 自增代际,旧 portal:* 键不再被读到、由 TTL 回收,和 RbacService 授权变更时的机制一致
    cache.IncrementAsync(CacheKeys.PortalGeneration, cancellationToken: cancellationToken);
```

这四个动作平时都用不上。正常的授权变更、字典/配置修改，内核自己就会同步失效对应的缓存。这个页面是留给**旁路场景**的逃生舱。有人直接改了库、跳过了 API，缓存和数据库对不上了，这时候才需要管理员手动点一下清掉。前端每个按钮都要二次确认，点完 toast 弹出被清理的条数。这就是照着「低频、有意识地执行」设计的，不是日常操作面板。

真遇到「数据库改了、页面却没变」的报障，多半就是这页开头说的那种旁路场景在发生：正常走 API 的写操作，内核自己已经把对应缓存失效了，用不着这四个按钮。按数据类型点对应的那个清理按钮，现象通常当场消失；清完还在，说明问题不在缓存，得回到数据库或应用日志继续查。

绕过服务改库的要是你自己的代码，比如迁移脚本、同步任务，就在代码里收敛，不必等管理员来点。`IConfigService.InvalidateAsync(key)` 失效一个配置键，由它合成的站点信息一起清；`IDictService.InvalidateAsync(typeCode)` 失效一类字典。两者分别广播 `ConfigChangedEvent` 和 `DictChangedEvent`，与走服务改值是同一条路。

不调也会自愈，只是要等过期：配置和字典的缓存跟权限码共用 `Cache:PermissionMinutes`，默认 20 分钟。同步水位这类运维会手改、每次都要读真值的行，别经缓存读，直接用 `IRepository<SysConfig>` 查库。缓存管理页上清配置缓存那一下是整批清，站点信息也在其中。

## 上线前过一遍的四处默认值

前面三个端点是出了事之后用的。这一节反过来，是出事之前该看一眼的四处配置。它们都有默认值，跑得起来，但默认值未必是你要的那个。

### 签名直链的有效期

`Upload:SignedUrlTtlMinutes`，**默认 0 = 不过期**。给了寿命之后，过期时刻会编进签名，改 URL 上的 `exp` 就验不过。

默认不给寿命，是因为直链会被存进公告正文这类持久内容里，配上寿命等于让它们到点集体坏掉。只把直链用在头像、临时预览这类不入正文的场景时，才该配它。

::: warning 一配上，存量链接立刻失效
包括已经存进库里的头像 URL。头像会在下次保存时重新签发，正文里的图片得自己迁移。
:::

### 操作日志的落库方式

`Logging:OpLog:Sync`，默认 `false`，也就是异步。请求线程只把填好上下文的行丢进一个有界队列，后台攒批插入（默认 200 行一批，停机前留 5 秒排空）。配成 `Sync=true` 则退回同步：那条 INSERT 挂在响应之前，每个写请求都要多等它一次数据库往返。

- `QueueCapacity`，默认 10000。满了丢最旧的：审计里新的比旧的重要，而阻塞请求线程去等一条日志是更坏的选择。逼近上限会打告警，不把丢行咽下去。
- `ParamMaxChars`，默认 8192。超限的入参只留开头并标注原长度，**结果仍是合法 JSON**，因为从中间切开的话日志详情页就解析不了了。不设这道上限的话，一次五千行的导入提交，整份数据都会原样躺进日志表。

要「落库失败即请求失败」，或者绝对不能丢行的部署，配 `Sync=true` 切成同步落库。

### 进程内缓存的上限

`Cache:MemoryEntryLimit` 默认 10 万条（`0` = 不限），`Cache:MaxEntryMinutes` 默认 1440 分钟（`0` = 不兜底）。内核用的是自己那一份 `MemoryCache`，与宿主 `AddMemoryCache()` 隔离，所以这两项只管内核写进去的条目，消费方缓存了多少不受牵连。

兜底过期是给 `PermissionMinutes=0` 那种配置留的下限。没有它，权限、范围、字典、配置这些键就真的永不过期，谁绕过服务直接改了库，那份缓存就一直陈旧下去。计数器不受兜底影响，也不会被容量压缩挤掉：门户代际号过期归零会让它退回上一代，限流计数被挤掉等于把闸门悄悄打开。

换了 Redis 之后这两项没有意义，淘汰归 Redis 自己的策略管。

### 口令哈希的迭代次数

`Security:Password:Pbkdf2Iterations`，默认 60 万，**低于 10 万按 10 万处理**。单次计算几十到一百毫秒，这个开销是故意的：暴力破解者要按次付费。但它同样是你自己的 CPU 账单，登录接口被刷时放大的是本机负载，所以弱硬件该能调低、强硬件该能调高。

调整不影响存量。哈希串里记着自己那次用的迭代数，老口令照旧验得过，并在下次登录时按新参数无感重算（明文只在那一刻在手上，错过就只能等用户自己改密）。`IPasswordHasher` 随之新增 `NeedsRehash`，默认接口实现返回 `false`，第三方实现不受影响。

::: tip 内核里没有字段级变更日志（DiffLog）
操作日志已经记了每个写请求的完整入参 JSON、操作人、时间、结果码。审计字段盖住每一行，就是 `CreateUserId`/`UpdateUserId`/`UpdateTime` 这几个。软删的行也还留着可查。字段级「改之前是什么、改之后是什么」这个能力，内核评估过后没做进核心。原因出在写入路径上：`IRepository<>` 没有单一的写入咽喉，Insert/Update/Delete 分头直调 SqlSugar，要补钩子就得在六个方法上都补。而且这类前像审计天然是「按需」的。消费方需要时，用 SqlSugar 原生的 `Aop.OnDiffLogEvent` 自己接就行，不必内核代劳。
:::
