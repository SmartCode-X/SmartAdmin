using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 追踪与指标出口。用的是 .NET 内置的 ActivitySource / Meter,零新增依赖——
/// 消费者要接 OpenTelemetry 只需在宿主里 <c>AddSource("SmartAdmin")</c>,不接也能用 dotnet-counters 看。
/// <para>这里验的是"名字与标签不漂":接线的一方是按字符串订阅的,改个名字对他们就是静默失灵。</para>
/// </summary>
public class DiagnosticsTests
{
    [Fact]
    public async Task 授权判定会开span并带上权限码与结果()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == SmartAdminDiagnostics.NAME,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            // 监听器是进程级的,并行跑的别的用例也在往这里灌 span,List 不能裸着写
            ActivityStopped = a => { lock (spans) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        // 模拟「另一个并行用例」:在本用例的根之前开,TraceId 必然不同,结果是 deny。
        // 几百个用例并行跑时这种 span 随时都在产,只取列表里第一个 authorize span 的写法
        // 会栽在这上面(Expected allow / Actual deny)。留着它,认领逻辑退化就会当场红。
        using (var noise = SmartAdminDiagnostics.Source.StartActivity("smartadmin.authorize"))
            noise?.SetTag("smartadmin.result", "deny");

        using var f = new AdminAppFactory();
        // TestServer 默认吞掉调用方的 ExecutionContext(PreserveExecutionContext=false),
        // Activity.Current 不打开这个开关就流不进被测管道,下面的 TraceId 也就对不上。
        f.Server.PreserveExecutionContext = true;

        // 本用例自己的根 Activity:内核的 span 挂在这个 TraceId 下。不按 TraceId 认领,
        // 就会捞到并行用例产的 smartadmin.authorize——那里面不乏 deny。
        using var root = new Activity("test-root").Start();

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Activity? span;
        lock (spans)
            span = spans.Find(a => a.OperationName == "smartadmin.authorize" && a.TraceId == root.TraceId);
        Assert.NotNull(span);
        Assert.Equal("allow", span!.GetTagItem("smartadmin.result"));
    }

    [Fact]
    public async Task 授权计数器按结果分标签()
    {
        var counted = new List<(string Instrument, long Value, string? Result)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SmartAdminDiagnostics.NAME) l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? result = null;
            foreach (var tag in tags)
                if (tag.Key == "result") result = tag.Value?.ToString();
            lock (counted) counted.Add((instrument.Name, value, result));
        });
        meterListener.Start();

        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        lock (counted)
        {
            Assert.Contains(counted, m => m.Instrument == "smartadmin.authorizations" && m.Result == "allow");
            Assert.Contains(counted, m => m.Instrument == "smartadmin.logins" && m.Result == "success");
        }
    }

    /// <summary>没有监听者时不该有任何开销痕迹:span 直接是 null,调用方用 <c>?.</c> 走过去。</summary>
    [Fact]
    public void 无监听者时不产生span()
    {
        Assert.Null(SmartAdminDiagnostics.StartActivity("smartadmin.authorize"));
    }
}

/// <summary>
/// 健康检查正文。默认写手只吐一个 <c>Healthy</c> 字面串——探针够用,人不够用:
/// ready 变红时看不出是数据库还是缓存,更看不出慢在哪一项。
/// </summary>
public class HealthJsonTests
{
    [Fact]
    public async Task 存活探针返回JSON状态()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("application/json", resp.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);

        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.Empty(json.GetProperty("checks").EnumerateArray());   // 存活探针不跑依赖检查
    }

    [Fact]
    public async Task 就绪探针逐项列出依赖与耗时()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // 状态码不变,编排层探针配置不用动

        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", json.GetProperty("status").GetString());

        var names = json.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("db", names);
        Assert.Contains("cache", names);
        Assert.DoesNotContain("level3-precheck", names);   // 那个恒 Healthy 的僵尸探针已随 Level3 一起删掉

        foreach (var check in json.GetProperty("checks").EnumerateArray())
        {
            Assert.Equal("Healthy", check.GetProperty("status").GetString());
            Assert.True(check.GetProperty("ms").GetInt64() >= 0);
        }
    }
}
