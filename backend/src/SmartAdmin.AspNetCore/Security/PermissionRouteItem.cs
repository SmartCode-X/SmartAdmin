namespace SmartAdmin.AspNetCore;

/// <summary>
/// 一条可授权路由(<c>MenuController.Routes</c> 的出参)——喂菜单表单里"权限码"字段的下拉(可多选,一个按钮挂多条)。
/// <c>Method</c>/<c>Path</c> 只为让人看得懂选的是哪个接口;真正写进菜单的是 <see cref="Code"/>。
/// </summary>
public record PermissionRouteItem
{
    /// <summary>权限码(= 规范化路由,经 <c>PermissionCode.Build</c> 算出,直接写进 <c>SysMenu.Permission</c>)</summary>
    public required string Code { get; init; }

    /// <summary>HTTP 方法(大写)</summary>
    public required string Method { get; init; }

    /// <summary>路由模板(原样,含 <c>{id}</c> 占位符)</summary>
    public required string Path { get; init; }
}
