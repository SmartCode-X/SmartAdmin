using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// 字典服务——字典类型/字典项的标准增删改查。字典项的启用列表是前端下拉高频读的数据,
/// <see cref="GetItemsByTypeAsync"/> 走读穿透缓存,增删改均显式失效对应类型的缓存。
/// </summary>
public interface IDictService
{
    /// <summary>分页查询字典类型,按编码/名称模糊过滤,按排序升序返回。</summary>
    Task<PagedList<SysDictType>> PageTypesAsync(DictTypePageInput input);

    /// <summary>按 Id 取字典类型,不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task<SysDictType> GetTypeAsync(long id);

    /// <summary>新增字典类型,Code 唯一(已存在抛 <see cref="ErrorCode.DictTypeCodeExists"/>),返回新 Id。</summary>
    Task<long> AddTypeAsync(DictTypeInput input);

    /// <summary>更新字典类型(仅 Name/Sort/Enabled/Remark 生效,Code 创建后不可变),不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task UpdateTypeAsync(long id, DictTypeInput input);

    /// <summary>删除字典类型(级联软删该类型下全部字典项),不存在抛 <see cref="ErrorCode.DictTypeNotFound"/>。</summary>
    Task DeleteTypeAsync(long id);

    /// <summary>批量删除字典类型(逐个级联删项 + 失效缓存);不存在的 Id 静默跳过。</summary>
    Task DeleteTypesBatchAsync(IReadOnlyCollection<long> ids);

    /// <summary>按类型编码取启用中的字典项列表(读穿透缓存,前端下拉的数据源)。</summary>
    Task<IReadOnlyList<SysDictItem>> GetItemsByTypeAsync(string typeCode);

    /// <summary>分页查询某类型下的字典项(<b>含停用</b>,管理端数据源;冷路径不走缓存),按排序升序返回。</summary>
    Task<PagedList<SysDictItem>> PageItemsAsync(DictItemPageInput input);

    /// <summary>新增字典项,返回新 Id。</summary>
    Task<long> AddItemAsync(DictItemInput input);

    /// <summary>更新字典项,不存在抛 <see cref="ErrorCode.DictItemNotFound"/>。</summary>
    Task UpdateItemAsync(long id, DictItemInput input);

    /// <summary>删除字典项(软删),不存在抛 <see cref="ErrorCode.DictItemNotFound"/>。</summary>
    Task DeleteItemAsync(long id);

    /// <summary>批量删除字典项(软删 + 失效所涉类型缓存);不存在的 Id 静默跳过。</summary>
    Task DeleteItemsBatchAsync(IReadOnlyCollection<long> ids);

    /// <summary>
    /// 失效某字典类型的项缓存并广播 <see cref="DictChangedEvent"/>。本服务自己的增删改已经会调它;
    /// 字典行被绕过本服务改掉(直接改库、迁移脚本、共库的别的系统)之后调它,
    /// 下一次 <see cref="GetItemsByTypeAsync"/> 就回库读新值。默认实现不带缓存,无事可做。
    /// </summary>
    Task InvalidateAsync(string typeCode) => Task.CompletedTask;
}
