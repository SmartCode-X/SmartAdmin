namespace SmartAdmin.Core;

/// <summary>
/// 缓存配置(对应 <c>SmartAdmin:Cache</c> 节)。
/// </summary>
public class AdminCacheOptions
{
    /// <summary>提供者:<c>Memory</c>(默认,进程内)| <c>Redis</c>(装 SmartAdmin.Caching.Redis 可选包后可用)。</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// Redis 连接串(Provider=Redis 时必填)。
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// 是否要求 Redis TLS。连接串未写 <c>ssl=true</c> 时,本项为 true 即强制 SSL 连接。
    /// </summary>
    public bool RequireTls { get; set; }

    /// <summary>缓存键前缀,默认 <c>smart:</c>(共享缓存实例时的命名空间隔离,所有逻辑键均以此打头)</summary>
    public string KeyPrefix { get; set; } = "smart:";

    /// <summary>
    /// 用户权限码缓存的过期分钟数,默认 20。授权变更走<b>显式失效</b>(即时),这个 TTL 只是兜底——
    /// 防止有人绕过服务直接改库导致缓存长期陈旧。设 0 表示永不过期(仅靠显式失效)。
    /// </summary>
    public int PermissionMinutes { get; set; } = 20;

    /// <summary>
    /// 进程内缓存最多存多少条,默认 10 万;<b>0 = 不限</b>。
    /// <para>内核用的是自己那一份 <c>MemoryCache</c>,与宿主 <c>AddMemoryCache()</c> 隔离,
    /// 所以这个上限只管内核写进去的条目,消费者自己缓存多少不受影响。</para>
    /// <para>超限时按 <c>MemoryCache</c> 的压缩策略淘汰;计数器标了不淘汰,不会被挤掉。
    /// 这一项只对进程内缓存有意义,换 Redis 后由 Redis 自己的淘汰策略负责。</para>
    /// </summary>
    public int MemoryEntryLimit { get; set; } = 100_000;

    /// <summary>
    /// 没有显式 TTL 的写入的兜底过期分钟数,默认 1440(一天);<b>0 = 不兜底</b>(永不过期)。
    /// <para><see cref="PermissionMinutes"/> 设 0 时,权限码、数据范围、字典、配置这些键就是永不过期的,
    /// 只靠显式失效——谁绕过服务直接改了库,那份缓存就一直陈旧下去。这一项是那种情况的下限。</para>
    /// <para>计数器不受它影响:门户代际号没有 TTL 且必须单调,过期归零会让它退回上一代。</para>
    /// </summary>
    public int MaxEntryMinutes { get; set; } = 1440;
}
