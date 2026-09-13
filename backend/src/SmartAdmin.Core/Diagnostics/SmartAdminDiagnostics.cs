using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartAdmin.Core;

/// <summary>
/// 内核的追踪与指标出口。用的是 .NET 内置的 <see cref="ActivitySource"/> / <see cref="Meter"/>,
/// <b>不引任何 NuGet</b>——没有监听者时这两样近乎零开销,消费者想接 OpenTelemetry 自己在宿主里挂:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(SmartAdminDiagnostics.NAME))
///     .WithMetrics(m => m.AddMeter(SmartAdminDiagnostics.NAME));
/// </code>
/// <para>不接也能看:<c>dotnet-counters monitor --counters SmartAdmin</c> 直接就有数。</para>
/// </summary>
public static class SmartAdminDiagnostics
{
    /// <summary>追踪源与仪表的名字(消费者接 OpenTelemetry 时按它订阅)。</summary>
    public const string NAME = "SmartAdmin";

    /// <summary>内核的追踪源。</summary>
    public static readonly ActivitySource Source = new(NAME);

    /// <summary>内核的仪表。</summary>
    public static readonly Meter Meter = new(NAME);

    /// <summary>授权判定次数,按结果分标签(<c>result</c>=allow|deny|unauthenticated|session-dead)。</summary>
    public static readonly Counter<long> Authorizations =
        Meter.CreateCounter<long>("smartadmin.authorizations", "{request}", "授权判定次数");

    /// <summary>登录次数,按结果分标签(<c>result</c>=success|failure)。</summary>
    public static readonly Counter<long> Logins =
        Meter.CreateCounter<long>("smartadmin.logins", "{login}", "登录次数");

    /// <summary>被限流拒绝的请求数。</summary>
    public static readonly Counter<long> RateLimited =
        Meter.CreateCounter<long>("smartadmin.rate_limited", "{request}", "被限流拒绝的请求数");

    /// <summary>定时任务执行次数,按结果分标签(<c>result</c>=success|failed|timeout)。</summary>
    public static readonly Counter<long> JobRuns =
        Meter.CreateCounter<long>("smartadmin.job_runs", "{run}", "定时任务执行次数");

    /// <summary>开一个内核内部的 span;没有监听者时返回 null,调用方用 <c>?.</c> 即可。</summary>
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        Source.StartActivity(name, kind);
}
