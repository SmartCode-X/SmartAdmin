using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// 内核自用的进程内缓存实例。<b>与宿主的 <c>AddMemoryCache()</c> 隔离</b>:设了 <c>SizeLimit</c> 之后,
/// 往这个实例里写的每一条都必须报体积,消费者自己缓存的东西不该被这条规矩牵连。
/// </summary>
internal sealed class KernelMemoryCache : MemoryCache
{
    public KernelMemoryCache(AdminCacheOptions options)
        : base(Options.Create(new MemoryCacheOptions
        {
            SizeLimit = options.MemoryEntryLimit > 0 ? options.MemoryEntryLimit : null,
        }))
    {
    }
}

/// <summary>
/// <see cref="ICacheProvider"/> 默认实现:进程内 <see cref="IMemoryCache"/>。
/// <para>单实例部署够用、零外部依赖;多实例共享缓存装 <c>SmartAdmin.Caching.Redis</c> 可选包前置替换。
/// 逻辑键统一追加 <see cref="AdminCacheOptions.KeyPrefix"/> 前缀,便于共享实例时按前缀隔离。</para>
/// <para>进程内缓存直接存对象引用、不序列化;故 <c>T</c> 原样进出(Redis 实现才涉及序列化)。</para>
/// </summary>
public class MemoryCacheProvider(IMemoryCache cache, AdminCacheOptions options) : ICacheProvider
{
    // 条带锁:原子取删/自增要序列化读-改-写,但限流每个请求都要自增一次——单把全局锁会让全站请求
    // 排队过同一个临界区。按 key 哈希分 64 桶,不同键互不阻塞;同键仍然是串行的(那正是要的语义)。
    private const int STRIPES = 64;
    private readonly Lock[] _stripes = [.. Enumerable.Range(0, STRIPES).Select(_ => new Lock())];

    private Lock StripeFor(string key) =>
        _stripes[(uint)StringComparer.Ordinal.GetHashCode(key) % STRIPES];

    private string Prefixed(string key) => options.KeyPrefix + key;

    /// <summary>写入选项:体积恒 1(上限即"最多存几条"),并给没指定 TTL 的写入一个兜底过期。</summary>
    private MemoryCacheEntryOptions EntryOptions(TimeSpan? expiry, bool counter)
    {
        var entry = new MemoryCacheEntryOptions { Size = 1 };
        if (expiry is { } ttl && ttl > TimeSpan.Zero)
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
        }
        else if (!counter && options.MaxEntryMinutes > 0)
        {
            // 没给 TTL 的写入(权限码、数据范围、字典、配置在 PermissionMinutes=0 时都是这种)
            // 若永不过期、只靠显式失效,有人绕过服务直接改库就会一直陈旧,所以给一个兜底上限。
            // 计数器不兜底:门户代际号没有 TTL 且必须单调,过期归零会让它退回上一代。
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.MaxEntryMinutes);
        }
        if (counter) entry.Priority = CacheItemPriority.NeverRemove;   // 计数器不该被容量压缩挤掉
        return entry;
    }

    /// <inheritdoc />
    public virtual Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(cache.TryGetValue(Prefixed(key), out var value) ? (T?)value : default);

    /// <inheritdoc />
    public virtual Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        cache.Set(Prefixed(key), value, EntryOptions(expiry, counter: false));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(Prefixed(key));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>条带锁保证读-改-写原子,并发失败计数不丢更新。</remarks>
    public virtual Task<long> IncrementAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        lock (StripeFor(full))
        {
            var next = (cache.TryGetValue(full, out var v) ? Convert.ToInt64(v) : 0L) + 1;
            cache.Set(full, next, EntryOptions(expiry, counter: true));
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    /// <remarks>条带锁保证取值与移除原子,并发下同一票据只有一个调用取得非空值。</remarks>
    public virtual Task<T?> GetAndRemoveAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var full = Prefixed(key);
        lock (StripeFor(full))
        {
            var value = cache.TryGetValue(full, out var v) ? (T?)v : default;
            cache.Remove(full);
            return Task.FromResult(value);
        }
    }
}
