using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 按客户端 IP 的固定窗口限流:全局一档 + 认证端点(<c>/api/v1/auth/*</c>)更严一档,
/// 超阈即 429 + 统一信封(<see cref="ErrorCode.TooManyRequests"/>)+ <c>Retry-After</c>。
///
/// <para><b>为什么自己数,而不用 ASP.NET 的 RateLimiter</b>:<c>PartitionedRateLimiter</c> 的窗口计数留在
/// <b>进程内的限流器对象</b>里 → N 个副本 = N × 阈值(认证桶默认 20/min,两副本就成了 40/min),
/// 认证防爆破的阈值被<b>静默</b>削半;它的分区选择器又是<b>同步</b>的,拿不到异步的分布式计数。
/// 计数走 <see cref="ICacheProvider.IncrementAsync"/>:装 Redis = 计数跨副本共享;不装 = 进程内计数。
/// <b>不新增任何抽象</b>——该方法本就用于登录失败计数等并发安全计数场景,内存/Redis 两个实现都是真原子。
/// 也用不上限流器的排队能力(<c>QueueLimit</c> 恒为 0,超阈即拒)。</para>
///
/// <para>阈值/开关从 <see cref="RuntimeRateLimit"/> 的快照<b>同步</b>读(避免每请求回查 4 个配置键);
/// 快照启动载入 + 订阅配置变更 + 定期重载(跨副本收敛)。</para>
///
/// <para>限流器跑在路由之前,按 <c>Request.Path</c> 直接区分认证端点(不依赖端点元数据)。
/// 客户端 IP 取 <c>Connection.RemoteIpAddress</c> —— 反代之后必须先经 <c>UseForwardedHeaders</c> 还原成真实客户端,
/// 否则全体用户共享同一个桶(见 <see cref="AdminForwardedHeadersOptions"/>)。</para>
///
/// <para>机器端另起一桶:带 API Key 头(<see cref="AdminApiKeyOptions.HeaderName"/>)的请求按 key 的哈希分区、
/// 用 <see cref="AdminRateLimitOptions.KeyPermitPerWindow"/> 计数,与用户端的 IP 桶互不影响——同一台网关后面
/// 的一堆设备不该分摊一个 IP 额度,一把 key 刷爆也不该殃及同 IP 的人。这里同样不依赖认证结果(中间件在认证之前),
/// 只看头在不在;错 key 照样按它自己的分区计数,然后在认证处被拒。</para>
///
/// <para>// ponytail: 固定窗口,窗口边界允许 2× 突发,是该算法本身的已知特性而非缺陷。
/// 要平滑就得改滑动窗口/令牌桶——那需要每键存一个结构而不只是计数,等真被边界突发咬到再说。</para>
/// </summary>
internal sealed class RateLimitMiddleware(
    RequestDelegate next,
    RuntimeRateLimit runtime,
    ICacheProvider cache,
    TimeProvider time,
    AdminSecurityOptions security)
{
    /// <summary>认证端点(登录/刷新/验证码):爆破的主战场,单独一档更严的阈值。</summary>
    private const string AUTH_PREFIX = "/api/v1/auth";

    public async Task InvokeAsync(HttpContext context)
    {
        var snapshot = runtime.Current;
        if (!snapshot.Enabled)
        {
            await next(context);
            return;
        }

        string bucket, partition;
        int permit;
        var apiKey = context.Request.Headers[security.ApiKey.HeaderName].ToString();
        if (apiKey.Length > 0)
        {
            // 机器端:按 key 分区。缓存键里放哈希,不让 key 明文进 Redis
            (bucket, partition, permit) = ("key", KeyPartition(apiKey), snapshot.KeyPermitPerWindow);
        }
        else
        {
            var isAuth = context.Request.Path.StartsWithSegments(AUTH_PREFIX);
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            (bucket, partition, permit) = (isAuth ? "auth" : "all", ip, isAuth ? snapshot.AuthPermitPerWindow : snapshot.PermitPerWindow);
        }
        if (permit <= 0)   // 该档位阈值 <=0 视为不限
        {
            await next(context);
            return;
        }

        var window = snapshot.WindowSeconds > 0 ? snapshot.WindowSeconds : 60;
        var nowSeconds = time.GetUtcNow().ToUnixTimeSeconds();

        // 窗口序号编进键 → 换窗口即换键、计数自然归零;旧键靠 TTL 回收,无需清理逻辑。
        var key = CacheKeys.RateLimit(bucket, partition, nowSeconds / window);
        var count = await cache.IncrementAsync(key, TimeSpan.FromSeconds(window), context.RequestAborted);

        if (count <= permit)
        {
            await next(context);
            return;
        }

        SmartAdminDiagnostics.RateLimited.Add(1);
        var retryAfter = (int)(window - nowSeconds % window);   // 距本窗口结束还有多久
        context.Response.Headers.RetryAfter = retryAfter.ToString();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            Result<object>.Fail(ErrorCode.TooManyRequests, new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter }),
            context.RequestAborted);
    }

    /// <summary>key 的 SHA-256 前 16 个十六进制字符:够分区、不可逆,缓存键里看不到 key 本身。</summary>
    private static string KeyPartition(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))[..16];
}
