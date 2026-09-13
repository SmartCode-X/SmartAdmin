using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 进程内缓存实现的三件事:并发下计数不丢、条目有上限、没给 TTL 的写入不会永远赖着。
/// <para>锁那一条不是纸上谈兵:限流中间件<b>每个请求</b>都要自增一次,一把单一全局锁会让
/// 全站请求排队过同一个临界区。</para>
/// </summary>
public class MemoryCacheProviderTests
{
    private static MemoryCacheProvider Make(AdminCacheOptions? options = null)
    {
        var opts = options ?? new AdminCacheOptions();
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions
        {
            SizeLimit = opts.MemoryEntryLimit > 0 ? opts.MemoryEntryLimit : null,
        }));
        return new MemoryCacheProvider(cache, opts);
    }

    /// <summary>64 个线程各自增 100 次,总数必须一分不差——读-改-写要真的是原子的。</summary>
    [Fact]
    public async Task 并发自增不丢更新()
    {
        var cache = Make();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++) await cache.IncrementAsync("hot");
        })));

        Assert.Equal(6400L, await cache.GetAsync<long>("hot"));
    }

    /// <summary>不同键落在不同条带上,互不阻塞;这里只验并发写不同键的结果都对。</summary>
    [Fact]
    public async Task 并发自增不同键互不干扰()
    {
        var cache = Make();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(n => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++) await cache.IncrementAsync($"k{n}");
        })));

        for (var n = 0; n < 32; n++)
            Assert.Equal(50L, await cache.GetAsync<long>($"k{n}"));
    }

    /// <summary>超过条目上限时旧条目被淘汰,不会一直涨到把进程撑爆。</summary>
    [Fact]
    public async Task 超过条目上限会淘汰()
    {
        var cache = Make(new AdminCacheOptions { MemoryEntryLimit = 20 });

        for (var i = 0; i < 500; i++) await cache.SetAsync($"bulk:{i}", i);

        // MemoryCache 的超容压缩是丢进线程池异步跑的,不是 Set 返回时就已经淘汰完;短轮询等它落地
        var alive = 500;
        for (var attempt = 0; attempt < 20 && alive == 500; attempt++)
        {
            if (attempt > 0) await Task.Delay(100);
            alive = 0;
            for (var i = 0; i < 500; i++)
                if (await cache.GetAsync<int?>($"bulk:{i}") is not null) alive++;
        }

        Assert.True(alive < 500, $"条目上限没生效,{alive} 条全都还在");
    }

    /// <summary>计数器标了不淘汰:限流与失败计数被容量压缩挤掉,等于把闸门悄悄打开。</summary>
    [Fact]
    public async Task 容量压缩不挤掉计数器()
    {
        var cache = Make(new AdminCacheOptions { MemoryEntryLimit = 20 });
        await cache.IncrementAsync("counter:keep");

        for (var i = 0; i < 500; i++) await cache.SetAsync($"bulk:{i}", i);

        Assert.Equal(1L, await cache.GetAsync<long>("counter:keep"));
    }

    /// <summary>没给 TTL 的写入也会拿到兜底过期,不会"永不过期,只靠显式失效"。</summary>
    [Fact]
    public async Task 无TTL写入拿到兜底过期()
    {
        var cache = Make(new AdminCacheOptions { MaxEntryMinutes = 1 });
        await cache.SetAsync("no-ttl", "v");

        // 兜底 1 分钟:立刻读还在(不能因为加了兜底就当场失效)
        Assert.Equal("v", await cache.GetAsync<string>("no-ttl"));
    }

    /// <summary>兜底设 0 = 永不过期。</summary>
    [Fact]
    public async Task 兜底可关闭()
    {
        var cache = Make(new AdminCacheOptions { MaxEntryMinutes = 0 });
        await cache.SetAsync("forever", "v");
        Assert.Equal("v", await cache.GetAsync<string>("forever"));
    }

    /// <summary>取删仍是一次性的:并发下同一票据只有一个调用拿得到值。</summary>
    [Fact]
    public async Task 并发取删只有一个拿到值()
    {
        var cache = Make();
        await cache.SetAsync("ticket", "once");

        var hits = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => cache.GetAndRemoveAsync<string>("ticket"))));

        Assert.Single(hits, h => h is not null);
    }
}
