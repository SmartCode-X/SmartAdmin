namespace SmartAdmin.Services;

/// <summary>
/// 消费者自定义高敏权限码维护(仅追加/删除自定义项;内核默认集不可删)。
/// </summary>
public interface IHighSensitivityPermissionService
{
    /// <summary>列出:内核默认(只读)+ 消费者追加项。</summary>
    Task<HighSensitivityPermissionList> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>追加自定义权限码;不可与默认集重复、不可重复追加。</summary>
    Task<SysHighSensitivityPermission> AddAsync(HighSensitivityPermissionInput input, long operatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>删除自定义追加项;禁止删除内核默认码。</summary>
    Task DeleteAsync(long id, long operatorUserId, CancellationToken cancellationToken = default);
}

/// <summary>高敏列表出参。</summary>
public record HighSensitivityPermissionList
{
    /// <summary>内核默认集(不可删)</summary>
    public IReadOnlyList<string> Defaults { get; init; } = [];

    /// <summary>消费者追加项</summary>
    public IReadOnlyList<HighSensitivityPermissionItem> Customs { get; init; } = [];
}

/// <summary>自定义高敏项展示。</summary>
public record HighSensitivityPermissionItem
{
    /// <summary>自定义追加项 Id。</summary>
    public long Id { get; init; }

    /// <summary>权限码(形如 <c>{METHOD}:/{路由}</c>,如 <c>POST:/api/v1/sys/user</c>)。</summary>
    public string PermissionCode { get; init; } = "";

    /// <summary>备注(可空)。</summary>
    public string? Remark { get; init; }
}

/// <summary>追加高敏权限入参。</summary>
public record HighSensitivityPermissionInput
{
    /// <summary>要追加的权限码(形如 <c>{METHOD}:/{路由}</c>)。</summary>
    public string PermissionCode { get; init; } = "";

    /// <summary>备注(可空)。</summary>
    public string? Remark { get; init; }
}
