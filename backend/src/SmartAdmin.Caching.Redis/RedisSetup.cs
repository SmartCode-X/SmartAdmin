using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;

namespace SmartAdmin.Caching.Redis;

/// <summary>
/// Redis 缓存装配(可选包入口)。在 <c>AddSmartAdmin()</c> <b>之前</b>调用即前置注册 <see cref="ICacheProvider"/>
/// 的 Redis 实现,压过内核默认的进程内 <c>MemoryCacheProvider</c>(内核用 <c>TryAdd</c> 注册,先到者胜)。
/// <para>缓存落 Redis 后,现有"变更即 <c>RemoveAsync</c> 失效"逻辑天然跨实例生效(共享键空间),多实例部署无需额外改动。</para>
/// </summary>
public static class RedisSetup
{
    /// <summary>
    /// 按配置启用 Redis 缓存:读 <c>SmartAdmin:Cache</c> 节,仅当 <c>Provider=Redis</c> 时注册 <see cref="RedisCacheProvider"/>;
    /// 否则空操作(保留内核 Memory 默认),给消费方"改 appsettings 一处开关"的体验。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSmartAdminRedisCache(builder.Configuration); // 先注册,赢 TryAdd
    /// builder.Services.AddSmartAdmin(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddSmartAdminRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("SmartAdmin:Cache").Get<AdminCacheOptions>() ?? new AdminCacheOptions();
        if (!string.Equals(options.Provider, "Redis", StringComparison.OrdinalIgnoreCase))
            return services;   // 未选 Redis:不接管,内核 Memory 默认生效
        return Register(services, options);
    }

    /// <summary>
    /// 显式用给定连接串启用 Redis 缓存(代码侧,不看配置的 <c>Provider</c> 开关——调用即启用)。
    /// </summary>
    public static IServiceCollection AddSmartAdminRedisCache(
        this IServiceCollection services, string connectionString, string keyPrefix = "smart:") =>
        Register(services, new AdminCacheOptions { Provider = "Redis", RedisConnectionString = connectionString, KeyPrefix = keyPrefix });

    private static IServiceCollection Register(IServiceCollection services, AdminCacheOptions options)
    {
        // 晚于内核调用 = TryAdd 落空、进程内缓存静默生效,多副本下强退/权限失效/限流计数全部各说各话;装配期就把顺序错报出来。
        if (services.Any(d => d.ServiceType == typeof(SmartAdminRegistered)))
            throw new InvalidOperationException(
                "AddSmartAdminRedisCache() 必须在 AddSmartAdmin() / AddSmartAdminWorker() 之前调用:内核已用 TryAdd 注册了进程内缓存,后注册的 Redis 实现不会生效。");

        // 装配期即校验连接串(fail-fast),而非等首次用缓存才在工厂里抛
        if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
            throw new InvalidOperationException("启用 Redis 缓存需配置 SmartAdmin:Cache:RedisConnectionString(或经重载显式传入连接串)。");

        // TryAdd:与内核可替换性模型一致——本方法须在 AddSmartAdmin() 之前调用方能胜出。
        services.TryAddSingleton<ICacheProvider>(_ => new RedisCacheProvider(options));
        return services;
    }
}
