namespace SmartAdmin.Core;

/// <summary>
/// 分页结果模型——所有分页查询的统一返回。ORM 中立(放 Core),
/// SqlSugar 侧的 <c>ToPagedListAsync</c> 扩展负责把查询物化成它。
/// </summary>
public class PagedList<T>
{
    /// <summary>当前页码(从 1 起)</summary>
    public int Current { get; init; }

    /// <summary>每页条数</summary>
    public int Size { get; init; }

    /// <summary>总记录数</summary>
    public int Total { get; init; }

    /// <summary>总页数(向上取整;Size 为 0 时为 0)</summary>
    public int Pages => Size <= 0 ? 0 : (Total + Size - 1) / Size;

    /// <summary>当前页数据</summary>
    public IReadOnlyList<T> Items { get; init; } = [];
}

/// <summary>
/// 分页入参基类。业务查询入参继承它再加自己的过滤字段(账号/名称/状态等)。
/// 字段命名与 <see cref="PagedList{T}"/> 保持一致:<c>Current</c>(页码)/ <c>Size</c>(页大小)。
/// <para>不是抽象类:没有额外过滤条件的列表端点可以直接把它当 <c>[FromQuery]</c> 入参用
/// (抽象时模型绑定构造不出实例,请求会 500)。</para>
/// </summary>
public record PageInputBase
{
    /// <summary>页码(从 1 起;≤0 归一为 1)</summary>
    public int Current { get; init; } = 1;

    /// <summary>每页条数(≤0 归一为默认 20;上限由查询扩展约束防超大分页)</summary>
    public int Size { get; init; } = 20;

    /// <summary>
    /// 排序字段(实体属性名,大小写不敏感)。仅当匹配实体真实列时生效,否则忽略回退默认排序——
    /// 绝不拼进 SQL,防注入(见 SqlSugar 侧 <c>ToPagedListAsync</c> 安全排序重载)。可空 = 不指定。
    /// </summary>
    public string? SortField { get; init; }

    /// <summary>排序方向:<c>asc</c> / <c>desc</c>(大小写不敏感);缺省/非法视为 <c>asc</c>。仅 SortField 生效时有意义。</summary>
    public string? SortOrder { get; init; }
}
