using System.Linq;
using SqlSugar;
using SmartAdmin.Core;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// SqlSugar 查询 → <see cref="PagedList{T}"/> 的物化扩展。
/// 一次查询同时取回当前页数据与总数(SqlSugar 的 <c>RefAsync</c> 机制),业务侧 <c>.ToPagedListAsync(input.Current, input.Size)</c> 即可。
/// </summary>
public static class PagedListExtensions
{
    /// <summary>单页最大条数的<b>默认值</b>——防止调用方传超大 Size 拖垮库(热路径保护)。</summary>
    public const int MAX_SIZE = 200;

    /// <summary>
    /// 当前生效的单页上限,由 <c>SmartAdmin:Api:MaxPageSize</c> 在装配时写入(未装配时为 <see cref="MAX_SIZE"/>)。
    /// <para>静态而非注入:分页是 <see cref="ISugarQueryable{T}"/> 的扩展方法,调用点拿不到 DI。</para>
    /// </summary>
    public static int MaxSize { get; set; } = MAX_SIZE;

    /// <summary>
    /// 物化成一页。<paramref name="size"/> 超过 <see cref="MaxSize"/> 时抛 <see cref="ErrorCode.PageSizeExceeded"/>。
    /// <para>超出上限直接抛错,而不是悄悄按上限截断:静默截断会让调用方拿到一个"成功但少了大半"的结果——
    /// 拿分页当导出用的地方会以为导全了,实际只导出了前 200 行。要更大的页请调 <c>SmartAdmin:Api:MaxPageSize</c>,
    /// 导全量请用 <c>Take(上限+1)</c> 这类显式写法,不要借道分页。</para>
    /// </summary>
    public static async Task<PagedList<T>> ToPagedListAsync<T>(this ISugarQueryable<T> query, int current, int size)
    {
        current = current <= 0 ? 1 : current;
        var max = MaxSize > 0 ? MaxSize : MAX_SIZE;
        AdminException.ThrowIf(size > max, ErrorCode.PageSizeExceeded,
            new Dictionary<string, object?> { ["size"] = size, ["max"] = max });
        size = size <= 0 ? 20 : size;

        // RefAsync<int> 是 SqlSugar 的"输出参数"载体:分页查询顺带把总数写回它,免二次 Count 往返
        RefAsync<int> total = 0;
        var items = await query.ToPageListAsync(current, size, total);

        return new PagedList<T> { Current = current, Size = size, Total = total, Items = items };
    }

    /// <summary>
    /// 客户端安全排序:<paramref name="input"/> 的 SortField/SortOrder 若匹配 <typeparamref name="T"/> 的真实列则据此排序(优先),
    /// 否则回退 <paramref name="defaultOrder"/>(业务默认排序),无默认则原样返回。返回排序后的查询(未物化),
    /// 故可放在 <c>.Select(投影)</c> 之前——此时按实体列校验/排序。安全:字段名走实体元数据校验 + OrderByPropertyName,
    /// 绝不把客户端字符串拼进 <c>OrderBy(string)</c>,方向仅 Asc/Desc,杜绝排序注入。
    /// </summary>
    public static ISugarQueryable<T> OrderBySafe<T>(
        this ISugarQueryable<T> query,
        PageInputBase input,
        Func<ISugarQueryable<T>, ISugarQueryable<T>>? defaultOrder = null)
    {
        if (!string.IsNullOrWhiteSpace(input.SortField))
        {
            // 白名单 = 实体真实列(属性名大小写不敏感);非法字段(不存在/含 SQL 片段)直接忽略
            var column = query.Context.EntityMaintenance.GetEntityInfo<T>().Columns
                .FirstOrDefault(c => string.Equals(c.PropertyName, input.SortField, StringComparison.OrdinalIgnoreCase));
            if (column is not null)
            {
                var desc = string.Equals(input.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
                // OrderByPropertyName 按实体元数据映射列名、内部参数安全;方向仅 Asc/Desc
                return query.OrderByPropertyName(column.PropertyName, desc ? OrderByType.Desc : OrderByType.Asc);
            }
        }
        return defaultOrder is not null ? defaultOrder(query) : query;
    }

    /// <summary>
    /// 带客户端安全排序的分页物化(无投影的直查场景):内部先 <see cref="OrderBySafe{T}"/> 再分页。
    /// 有 <c>.Select(投影)</c> 的场景请在 Select 前手动调 <see cref="OrderBySafe{T}"/>(按实体列排序)。
    /// </summary>
    public static Task<PagedList<T>> ToPagedListAsync<T>(
        this ISugarQueryable<T> query,
        PageInputBase input,
        Func<ISugarQueryable<T>, ISugarQueryable<T>>? defaultOrder = null) =>
        query.OrderBySafe(input, defaultOrder).ToPagedListAsync(input.Current, input.Size);
}
