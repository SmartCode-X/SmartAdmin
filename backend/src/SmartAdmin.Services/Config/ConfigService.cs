using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IConfigService"/> 默认实现。<see cref="GetValueByKeyAsync"/> 走读穿透缓存:
/// 命中直接返回;未命中查库,把值(<c>null</c> 归一为空串,使"缓存过空值"与"未缓存"可区分)写入缓存后返回;
/// 键根本不存在则不缓存,避免把"配置项不存在"这一状态长期钉死在缓存里。
/// </summary>
public class ConfigService(
    IRepository<SysConfig> configs,
    ICacheProvider cache,
    AdminCacheOptions cacheOptions,
    IEventBus events) : IConfigService
{
    /// <inheritdoc />
    public virtual async Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input)
    {
        var excludedGroups = input.ExcludedGroupCodes?
            .Where(static group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var query = configs.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Name), c => c.Name.Contains(input.Name!))
            .WhereIF(!string.IsNullOrEmpty(input.ConfigKey), c => c.ConfigKey.Contains(input.ConfigKey!))
            .WhereIF(!string.IsNullOrEmpty(input.GroupCode), c => c.GroupCode == input.GroupCode);

        // 空分组也是消费方可管理的自定义配置,不能因 SQL 的 NULL NOT IN 语义被误滤掉。
        if (excludedGroups.Length > 0)
            query = query.Where(c => c.GroupCode == null || !excludedGroups.Contains(c.GroupCode!));

        return await query.OrderBy(c => c.Sort).ToPagedListAsync(input.Current, input.Size);
    }

    /// <inheritdoc />
    public virtual async Task<SysConfig> GetAsync(long id)
    {
        var config = await configs.GetByIdAsync(id);
        AdminException.ThrowIf(config is null, ErrorCode.ConfigNotFound);
        return config!;
    }

    /// <inheritdoc />
    public virtual async Task<string?> GetValueByKeyAsync(string key)
    {
        var cacheKey = CacheKeys.Config(key);
        var cached = await cache.GetAsync<string>(cacheKey);
        if (cached is not null) return cached;

        var config = await configs.GetFirstAsync(c => c.ConfigKey == key);
        if (config is null) return null; // 键不存在,不缓存

        var value = config.ConfigValue ?? "";
        var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
        await cache.SetAsync(cacheKey, value, ttl);
        return value;
    }

    /// <inheritdoc />
    public virtual async Task<SiteInfoOutput> GetSiteInfoAsync()
    {
        // 整体缓存一份:这是匿名端点,每次打开登录页都要读,拆成七个键就是七次串行往返
        var cached = await cache.GetAsync<SiteInfoOutput>(CacheKeys.SiteInfo);
        if (cached is not null) return cached;

        var info = await LoadSiteInfoAsync();
        var ttl = cacheOptions.PermissionMinutes > 0 ? TimeSpan.FromMinutes(cacheOptions.PermissionMinutes) : (TimeSpan?)null;
        await cache.SetAsync(CacheKeys.SiteInfo, info, ttl);
        return info;
    }

    /// <summary>逐键装配站点信息(仅缓存未命中时执行)。</summary>
    protected virtual async Task<SiteInfoOutput> LoadSiteInfoAsync() => new()
    {
        Title = await GetValueByKeyAsync(ConfigSeed.SITE_TITLE_KEY),
        Subtitle = await GetValueByKeyAsync(ConfigSeed.SITE_SUBTITLE_KEY),
        Copyright = await GetValueByKeyAsync(ConfigSeed.SITE_COPYRIGHT_KEY),
        CopyrightUrl = await GetValueByKeyAsync(ConfigSeed.SITE_COPYRIGHT_URL_KEY),
        Logo = await GetValueByKeyAsync(ConfigSeed.SITE_LOGO_KEY),
        // 匿名暴露验证码开关(不含类型等内部细节),供登录页决定是否渲染验证码;缺失即视为关。
        CaptchaEnabled = bool.TryParse(await GetValueByKeyAsync(CaptchaService.KEY_ENABLED), out var e) && e,
        // 匿名暴露短信免密登录开关,供登录页决定是否渲染短信登录入口;缺失即视为关。MFA 不在此暴露(由登录 40009 信令带内下发)。
        SmsLoginEnabled = bool.TryParse(await GetValueByKeyAsync(SmsOtpService.KEY_LOGIN_ENABLED), out var s) && s,
    };

    /// <inheritdoc />
    // ponytail: 少量键逐条查改足够;键集变大再合并成 IN 查询 + 批量更新。
    public virtual async Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items)
    {
        foreach (var item in items)
        {
            var entity = await configs.GetFirstAsync(c => c.ConfigKey == item.ConfigKey);
            if (entity is null)
            {
                // 第三方登录运营键:配置中心 Tab 可能对「已注册但尚未种子」的 code 写 enabled,
                // 允许按键自动落库(GroupCode=externalauth);其它未知键仍忽略。
                if (!item.ConfigKey.StartsWith("sys.externalauth.", StringComparison.Ordinal))
                    continue;
                // 并发首配:两请求都查不到 → 双插撞 ConfigKey 唯一索引;冲突后改为重读并更新。
                try
                {
                    await configs.InsertAsync(new SysConfig
                    {
                        ConfigKey = item.ConfigKey,
                        ConfigValue = item.ConfigValue,
                        Name = item.ConfigKey,
                        GroupCode = "externalauth",
                        Sort = 80,
                        Remark = "第三方登录运营项(配置中心写入)",
                    });
                }
                catch (Exception ex) when (LooksLikeUniqueKeyViolation(ex))
                {
                    entity = await configs.GetFirstAsync(c => c.ConfigKey == item.ConfigKey);
                    if (entity is null) throw;
                    entity.ConfigValue = item.ConfigValue;
                    await configs.UpdateAsync(entity);
                }
                await InvalidateAsync(item.ConfigKey);
                continue;
            }
            entity.ConfigValue = item.ConfigValue;
            await configs.UpdateAsync(entity);
            await InvalidateAsync(entity.ConfigKey);
        }
    }

    /// <summary>跨 SQLite/MySQL/SqlServer/PG 的唯一键冲突粗判(消息/SqlState 启发式)。</summary>
    public static bool LooksLikeUniqueKeyViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message ?? "";
            if (m.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || m.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || m.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || m.Contains("2627") // SQL Server unique constraint
                || m.Contains("2601") // SQL Server unique index
                || m.Contains("23505")) // PostgreSQL unique_violation
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(ConfigInput input)
    {
        // 查重纳入软删行:唯一索引覆盖已软删行,漏检会撞库唯一约束抛原生 500。已软删的键视为永久保留。
        AdminException.ThrowIf(
            await configs.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(c => c.ConfigKey == input.ConfigKey),
            ErrorCode.ConfigKeyExists);

        var entity = new SysConfig
        {
            ConfigKey = input.ConfigKey,
            ConfigValue = input.ConfigValue,
            Name = input.Name,
            GroupCode = input.GroupCode,
            Sort = input.Sort,
            Remark = input.Remark,
        };
        await configs.InsertAsync(entity);
        await InvalidateAsync(entity.ConfigKey);
        return entity.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, ConfigInput input)
    {
        var entity = await GetAsync(id);
        entity.ConfigValue = input.ConfigValue;
        entity.Name = input.Name;
        entity.GroupCode = input.GroupCode;
        entity.Sort = input.Sort;
        entity.Remark = input.Remark;
        await configs.UpdateAsync(entity);
        await InvalidateAsync(entity.ConfigKey);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var config = await GetAsync(id);
        AdminException.ThrowIf(config.Id < 1000, ErrorCode.SeedDataProtected);
        await configs.DeleteAsync(id);
        await InvalidateAsync(config.ConfigKey);
    }

    /// <inheritdoc />
    public virtual async Task InvalidateAsync(string key)
    {
        // 站点信息是若干个配置键的合成值,分不清改的是不是其中之一就一律清掉——
        // 它极低频改动,而漏清一次就是登录页挂着旧标题不放。
        await cache.RemoveManyAsync([CacheKeys.Config(key), CacheKeys.SiteInfo]);
        await events.PublishAsync(new ConfigChangedEvent(key));
    }
}
