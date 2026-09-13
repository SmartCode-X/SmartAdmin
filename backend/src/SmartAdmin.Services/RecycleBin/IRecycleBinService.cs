using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>回收站:按登记的实体类型查看软删数据、恢复或彻底删除。</summary>
public interface IRecycleBinService
{
    /// <summary>分页列出某类型的已删记录。</summary>
    Task<PagedList<RecycleBinItem>> PageAsync(string type, RecycleBinPageInput input);

    /// <summary>恢复一行;未命中抛 <see cref="ErrorCode.RecycleNotFound"/>。</summary>
    Task RestoreAsync(string type, long id);

    /// <summary>彻底删除一行(不可恢复);未命中抛 <see cref="ErrorCode.RecycleNotFound"/>。</summary>
    Task PurgeAsync(string type, long id);
}

/// <inheritdoc cref="IRecycleBinService"/>
public class RecycleBinService(IEnumerable<RecycleBinType> types, IServiceProvider sp) : IRecycleBinService
{
    /// <inheritdoc />
    public virtual Task<PagedList<RecycleBinItem>> PageAsync(string type, RecycleBinPageInput input) =>
        Resolve(type).PageAsync(sp, input);

    /// <inheritdoc />
    public virtual async Task RestoreAsync(string type, long id) =>
        AdminException.ThrowIf(await Resolve(type).RestoreAsync(sp, id) == 0, ErrorCode.RecycleNotFound);

    /// <inheritdoc />
    public virtual async Task PurgeAsync(string type, long id) =>
        AdminException.ThrowIf(await Resolve(type).PurgeAsync(sp, id) == 0, ErrorCode.RecycleNotFound);

    /// <summary>路由段 → 登记项(大小写不敏感);未登记的类型抛 <see cref="ErrorCode.RecycleInvalidType"/>。</summary>
    protected virtual RecycleBinType Resolve(string type) =>
        types.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase))
        ?? throw new AdminException(ErrorCode.RecycleInvalidType);
}

/// <summary>回收站类型登记的便捷入口。</summary>
public static class RecycleBinSetup
{
    /// <summary>
    /// 把一张软删表接进回收站:<c>services.AddRecycleBinType&lt;Wo&gt;("wo", w =&gt; w.Name, w =&gt; w.Code)</c>。
    /// <para>按实现类型去重(同一实体的泛型登记项算同一个),故重复调用无害。要换掉内核某一类型的行为,
    /// 继承对应的登记类并在 <c>AddSmartAdmin()</c> 之前注册——先到者胜。</para>
    /// </summary>
    public static IServiceCollection AddRecycleBinType<TEntity>(
        this IServiceCollection services,
        string type,
        Func<TEntity, string> name,
        Func<TEntity, string?>? code = null) where TEntity : BaseEntity, new()
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<RecycleBinType>(new RecycleBinType<TEntity>(type, name, code)));
        return services;
    }
}
