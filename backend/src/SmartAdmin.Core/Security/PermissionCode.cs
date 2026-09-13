namespace SmartAdmin.Core;

/// <summary>
/// 权限码的<b>唯一规范化真源</b>:<c>{大写 METHOD}:/{小写路由模板}</c>,如 <c>GET:/api/v1/ping</c>。
/// <para>用路由模板而非实际路径——带参数的路由(<c>user/{id}</c>)权限码稳定,不随参数值变化。</para>
/// <para>授权比对(<c>RolePermissionAttribute</c>)、路由清单(<c>MenuController.Routes</c>)、操作日志缺省操作名、
/// 菜单保存时的归一化、用户权限码聚合,全部经这里算码,防止"授权时算的码"与"菜单里存的码"因大小写、
/// 前导斜杠差一个字符而静默对不上(授了权、点了 403,且无任何报错)。public 是刻意的——消费方自建控制器时同样能算出自己的码。</para>
/// <para><b>一个菜单按钮节点可挂多条权限码</b>:以 <see cref="Separator"/> 连接存在 <c>SysMenu.Permission</c> 里,
/// 角色勾选该按钮即一并授出。授权单元是按钮(一种能力,如「用户-查询」),执行单元是路由,二者一对多;
/// 后端仍逐条路由比对,只是一个勾选框展开成多条路由。</para>
/// </summary>
public static class PermissionCode
{
    /// <summary>多条权限码在 <c>SysMenu.Permission</c> 里的分隔符。</summary>
    public const char Separator = ';';

    /// <summary>按规范拼权限码;<paramref name="routeTemplate"/> 为空时返回 <c>{METHOD}:/</c>(不会匹配任何授权)。</summary>
    public static string Build(string httpMethod, string? routeTemplate) =>
        $"{httpMethod.ToUpperInvariant()}:/{(routeTemplate ?? "").TrimStart('/').ToLowerInvariant()}";

    /// <summary>
    /// 规范化一条手写的码:按首个冒号拆出 Method 与路由模板再经 <see cref="Build"/> 重拼,
    /// 于是 <c>get:/API/v1/Ping</c> 与 <c>GET:/api/v1/ping</c> 同码。没有冒号的原样返回(它不会匹配任何端点)。
    /// </summary>
    public static string Normalize(string code)
    {
        var i = code.IndexOf(':');
        return i <= 0 ? code : Build(code[..i], code[(i + 1)..]);
    }

    /// <summary>
    /// 把一个节点的 <c>Permission</c> 字段拆成规范化的码集合:按 <see cref="Separator"/> 拆、去空白、
    /// 逐条 <see cref="Normalize"/>、去重、保持原顺序。空串 / 目录节点得到空数组。
    /// </summary>
    public static string[] Split(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var codes = new List<string>();
        foreach (var raw in permission.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = Normalize(raw);
            if (seen.Add(code)) codes.Add(code);
        }
        return [.. codes];
    }

    /// <summary>把码集合拼回 <c>Permission</c> 字段的存储形态(<see cref="Split"/> 的逆运算)。</summary>
    public static string Join(IEnumerable<string> codes) => string.Join(Separator, codes);
}
