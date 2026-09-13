using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 会话热路径的缓存往返次数。每个受保护请求都要过这一段,所以它的成本是<b>乘以全站 QPS</b> 的——
/// 装了 Redis 之后每一次往返都是一个网络 RTT,省下来的不是"几次内存读"。
/// <para>没有这条锁,一个请求很容易累积到四次会话相关的缓存访问:活跃校验读一次、活动追踪<b>再读一次同一个键</b>、
/// 回写一次、外加一个独立节流键的读。这里把次数钉死,防它悄悄涨回去。</para>
/// </summary>
public class SessionHotPathTests
{
    /// <summary>稳态(令牌已发、缓存已热)下,一个受保护请求只读一次会话缓存,一次都不写。</summary>
    [Fact]
    public async Task 稳态请求只读一次会话缓存且不回写()
    {
        var counter = new CountingCacheProvider();
        using var f = new AdminAppFactory
        {
            Overrides = s =>
            {
                s.RemoveAll<ICacheProvider>();
                s.AddSingleton<ICacheProvider>(counter);
            },
        };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        // 第一个请求会把 LastPersistedAt 记进去(那一次必然有回写),从第二个起才是稳态
        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");
        counter.Reset();

        await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Assert.Equal(1, counter.Gets("session:"));
        Assert.Equal(0, counter.Sets("session:"));
    }

    /// <summary>节流窗内不该有第二次落库,更不该为此再读一个独立的节流键。</summary>
    [Fact]
    public async Task 节流窗内不再碰独立节流键()
    {
        var counter = new CountingCacheProvider();
        using var f = new AdminAppFactory
        {
            Overrides = s =>
            {
                s.RemoveAll<ICacheProvider>();
                s.AddSingleton<ICacheProvider>(counter);
            },
        };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        for (var i = 0; i < 3; i++)
            await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");

        Assert.Equal(0, counter.Gets("session:act:"));
        Assert.Equal(0, counter.Sets("session:act:"));
    }
}

/// <summary>数键次数的缓存壳子,内部仍走真实的进程内实现(要的是次数,不是桩行为)。</summary>
internal sealed class CountingCacheProvider : ICacheProvider
{
    // 声明成接口:RemoveManyAsync 是默认接口实现,只能经接口调用
    private readonly ICacheProvider _inner = new MemoryCacheProvider(
        new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
        new AdminCacheOptions());

    private readonly ConcurrentBag<(string Op, string Key)> _ops = [];

    public void Reset() => _ops.Clear();

    public int Gets(string prefix) => Count("get", prefix);

    public int Sets(string prefix) => Count("set", prefix);

    /// <summary>单键删除的次数(批量删不计入)。</summary>
    public int Removes(string prefix) => Count("remove", prefix);

    private int Count(string op, string prefix) =>
        _ops.Count(x => x.Op == op && x.Key.StartsWith(prefix, StringComparison.Ordinal));

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ops.Add(("get", key));
        return _inner.GetAsync<T>(key, cancellationToken);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        _ops.Add(("set", key));
        return _inner.SetAsync(key, value, expiry, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _ops.Add(("remove", key));
        return _inner.RemoveAsync(key, cancellationToken);
    }

    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        _ops.Add(("removeMany", ""));
        return _inner.RemoveManyAsync(keys, cancellationToken);
    }

    /// <summary>批量删被调用的次数(不是被删的键数)。</summary>
    public int RemoveManyCalls => _ops.Count(x => x.Op == "removeMany");

    public Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        _ops.Add(("incr", key));
        return _inner.IncrementAsync(key, expiry, cancellationToken);
    }

    public Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ops.Add(("getdel", key));
        return _inner.GetAndRemoveAsync<T>(key, cancellationToken);
    }
}
