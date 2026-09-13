using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 回收站里的一类实体。内核的九种是登记项而不是硬编码分支,这是一个可扩展点——消费者用
/// <c>services.AddRecycleBinType&lt;MyEntity&gt;("my", e =&gt; e.Name)</c> 一行就能把自己的软删表接进来,
/// 不必修改内核代码。
/// </summary>
public abstract class RecycleBinType
{
    /// <summary>路由段(小写),如 <c>user</c> → <c>/api/v1/sys/recycle/user/page</c>。</summary>
    public abstract string Type { get; }

    /// <summary>对应实体类型。</summary>
    public abstract Type EntityType { get; }

    /// <summary>分页列出已删行。</summary>
    public abstract Task<PagedList<RecycleBinItem>> PageAsync(IServiceProvider sp, RecycleBinPageInput input);

    /// <summary>恢复一行,返回受影响行数。</summary>
    public abstract Task<int> RestoreAsync(IServiceProvider sp, long id);

    /// <summary>彻底删除一行,返回受影响行数。</summary>
    public abstract Task<int> PurgeAsync(IServiceProvider sp, long id);
}

/// <summary>
/// 泛型登记项:给出路由段与名称/编码取值即可。需要恢复后失效缓存、彻底删除前清关联这类附加动作时,
/// 继承本类覆写 <see cref="AfterRestoreAsync"/> / <see cref="BeforePurgeAsync"/>(内核的用户、角色、任务就是这么做的)。
/// </summary>
public class RecycleBinType<TEntity>(
    string type,
    Func<TEntity, string> name,
    Func<TEntity, string?>? code = null) : RecycleBinType
    where TEntity : BaseEntity, new()
{
    /// <inheritdoc />
    public override string Type { get; } = type;

    /// <inheritdoc />
    public override Type EntityType => typeof(TEntity);

    /// <summary>已删行查询。覆写点:叠加数据范围等额外条件(内核的用户类型就在这里叠范围)。</summary>
    protected virtual ISugarQueryable<TEntity> Query(IServiceProvider sp) =>
        sp.GetRequiredService<ISqlSugarClient>().Queryable<TEntity>()
            .ClearFilter<ISoftDelete>()
            .Where(e => e.IsDelete == true);

    /// <summary>仓储(恢复/硬删走它,保留唯一列后缀逆转等既有语义)。</summary>
    protected static IRepository<TEntity> Repo(IServiceProvider sp) => sp.GetRequiredService<IRepository<TEntity>>();

    /// <inheritdoc />
    public override async Task<PagedList<RecycleBinItem>> PageAsync(IServiceProvider sp, RecycleBinPageInput input)
    {
        var paged = await Query(sp).OrderByDescending(e => e.UpdateTime).ToPagedListAsync(input.Current, input.Size);
        return new PagedList<RecycleBinItem>
        {
            Current = paged.Current,
            Size = paged.Size,
            Total = paged.Total,
            Items = [.. paged.Items.Select(e => new RecycleBinItem(e.Id, name(e), code?.Invoke(e), e.UpdateTime, e.UpdateUserId))],
        };
    }

    /// <inheritdoc />
    public override async Task<int> RestoreAsync(IServiceProvider sp, long id)
    {
        var rows = await Repo(sp).RestoreAsync(id);
        if (rows > 0) await AfterRestoreAsync(sp, id);
        return rows;
    }

    /// <inheritdoc />
    public override async Task<int> PurgeAsync(IServiceProvider sp, long id)
    {
        await BeforePurgeAsync(sp, id);
        return await Repo(sp).HardDeleteAsync(id);
    }

    /// <summary>恢复成功后的附加动作(失效缓存、重置状态等)。</summary>
    protected virtual Task AfterRestoreAsync(IServiceProvider sp, long id) => Task.CompletedTask;

    /// <summary>彻底删除前的附加动作(清关联行等)。</summary>
    protected virtual Task BeforePurgeAsync(IServiceProvider sp, long id) => Task.CompletedTask;
}
