using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// 系统配置服务——键值对配置的标准 CRUD + 分页查询,外加供业务方高频读取的
/// <see cref="GetValueByKeyAsync"/>(读穿透缓存)。
/// </summary>
public interface IConfigService
{
    /// <summary>分页查询配置,按名称/键模糊过滤 + 分组精确/排除过滤,按排序升序返回。</summary>
    Task<PagedList<SysConfig>> PageAsync(ConfigPageInput input);

    /// <summary>按 Id 取单条,不存在抛 <see cref="ErrorCode.ConfigNotFound"/>。</summary>
    Task<SysConfig> GetAsync(long id);

    /// <summary>
    /// 按配置键取值(读穿透缓存):命中缓存直接返回;未命中查库并回填缓存;键不存在则返回 null(不缓存)。
    /// </summary>
    Task<string?> GetValueByKeyAsync(string key);

    /// <summary>取站点信息(匿名展示白名单:仅站点标题等安全暴露的键,不泄露任意配置)。</summary>
    Task<SiteInfoOutput> GetSiteInfoAsync();

    /// <summary>
    /// 批量按键回写配置<b>值</b>(分类配置中心结构化表单用):仅更新已存在的键,未知键忽略;
    /// 不改 Name/GroupCode/Sort;逐键失效缓存并广播变更。
    /// </summary>
    Task SaveValuesAsync(IReadOnlyCollection<ConfigBatchItem> items);

    /// <summary>新增配置,键已存在抛 <see cref="ErrorCode.ConfigKeyExists"/>,返回新 Id。</summary>
    Task<long> AddAsync(ConfigInput input);

    /// <summary>更新配置(不含 <c>ConfigKey</c>,键创建后不可改);不存在抛 <see cref="ErrorCode.ConfigNotFound"/>。</summary>
    Task UpdateAsync(long id, ConfigInput input);

    /// <summary>删除配置(软删,不存在抛 <see cref="ErrorCode.ConfigNotFound"/>)。</summary>
    Task DeleteAsync(long id);

    /// <summary>
    /// 失效某个配置键的缓存(连同由它合成的站点信息)并广播 <see cref="ConfigChangedEvent"/>。
    /// 本服务自己的增删改已经会调它;配置行被绕过本服务改掉(直接改库、迁移脚本、共库的别的系统)之后调它,
    /// 下一次 <see cref="GetValueByKeyAsync"/> 就回库读新值。默认实现不带缓存,无事可做。
    /// </summary>
    Task InvalidateAsync(string key) => Task.CompletedTask;
}
