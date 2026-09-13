# 追踪与指标

内核会吐追踪 span 和指标，出口是一个 `ActivitySource` 加一个 `Meter`，两者同名 `SmartAdmin`。用的是 .NET 内置的诊断 API，没有新增任何依赖，所以它没有开关：没人监听时 `StartActivity` 返回 null，代价就是一次判空。

## 出口只有一个名字

```csharp
public static class SmartAdminDiagnostics
{
    public const string NAME = "SmartAdmin";

    public static readonly ActivitySource Source = new(NAME);
    public static readonly Meter Meter = new(NAME);
}
```

追踪源和仪表共用这一个名字，订阅时也就只需要记住它一个。

## 四个计数器

| 计数器 | 标签 | 记什么 |
| --- | --- | --- |
| `smartadmin.authorizations` | `result` = `allow` \| `deny` \| `unauthenticated` \| `session-dead` | 每次经 `[RolePermission]` 的授权判定 |
| `smartadmin.logins` | `result` = `success` \| `failure`；失败时另带 `code` | 每次登录尝试 |
| `smartadmin.rate_limited` | 无 | 被限流拒掉的请求 |
| `smartadmin.job_runs` | `result` = 执行状态（`Success` / `Failed` / `Timeout` …） | 每次定时任务执行收尾 |

标签是这四个计数器的全部价值所在。`smartadmin.authorizations` 的总量只能告诉你系统忙不忙，而 `deny` 那条曲线突然抬头，说明有人在扫端点；`session-dead` 抬头，说明刚踢了一批人或者会话寿命配短了。登录失败带上 `code`，则能把「密码错」和「验证码错」分开看，撞库和用户手滑长得不一样。

## 授权 span

`[RolePermission]` 每次判定开一个 `smartadmin.authorize` span，带两个标签：

| 标签 | 值 |
| --- | --- |
| `smartadmin.permission_code` | 该端点的权限码，如 `GET:/api/v1/sys/user/page`。超管与未认证请求不会走到这一步，因此没有这个标签 |
| `smartadmin.result` | 与计数器的 `result` 同一套取值 |

权限码进了 span，「谁被拒在哪个端点上」在 APM 里就是一次筛选，不用再去翻日志对时间戳。

## 接出去

接 OpenTelemetry 的话，宿主里按名字订阅：

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(SmartAdminDiagnostics.NAME))
    .WithMetrics(m => m.AddMeter(SmartAdminDiagnostics.NAME));
```

不接也能看。`dotnet-counters` 直接连进程，不用改一行代码，也不用重启：

```bash
dotnet-counters monitor --process-id <pid> --counters SmartAdmin
```

现场只有一台机器、只想知道「刚才那波 403 是不是同一个人」的时候，这条命令比架一整套采集链路快得多。

## 异常日志的 `TraceId` 优先取 W3C trace id

`sys_exception_log` 的 `TraceId` 优先取 `Activity.Current` 的 W3C trace id，取不到才回退 `HttpContext.TraceIdentifier`：

```csharp
TraceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier,
```

回退的那个值形如 `0HN7GK2M8QJ1B:00000003`，是本进程内的「连接、序号」，出了这台机器没人认得。W3C trace id 不一样，它由整条调用链共享，拿着它去 APM 里搜，能直接搜出这次异常前后网关、前端、下游服务各做了什么。**接了追踪之后这一列才有跨进程的意义**，没接就是进程内标识，不会变空。

## 健康检查的正文是 JSON

`/health` 与 `/health/ready` 的响应正文是 JSON。**状态码按惯例走**：Healthy 是 200，其余是 503，所以编排层探针按状态码判定就够了；脚本要判具体状态，读正文的 `status` 字段。

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

`/health` 只报进程存活，不跑任何依赖检查，它的 `checks` 恒为空数组。逐项内容只在 `/health/ready` 上有。

只有状态码，探针够用、人不够用：ready 变红时看不出是数据库还是缓存，更看不出慢在哪一项。所以正文逐项给出状态和耗时：

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

`error` 只给异常的类型名，不给消息。健康检查是匿名端点，而异常消息里常常带着连接串、主机名、库名。

两个探针分别对应 k8s 的哪种 probe、反代该怎么放行，在[部署指南](/zh/guide/deployment/)里讲。
