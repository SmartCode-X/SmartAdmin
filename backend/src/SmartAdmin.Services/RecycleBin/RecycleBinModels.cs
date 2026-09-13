using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>回收站统一 DTO</summary>
public record RecycleBinItem(long Id, string Name, string? Code, DateTime? DeletedAt, long? DeletedBy);

/// <summary>回收站分页入参</summary>
public record RecycleBinPageInput : PageInputBase;
