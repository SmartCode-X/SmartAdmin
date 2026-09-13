using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 权限码一致性回归锁:DefaultMenuSeed 里手写的权限码与内置控制器的 [RolePermission] 端点必须<b>双向</b>对得上。
/// 种子码与路由是两处分离的手写串,靠人肉同步;改错一个字符即"授了也匹配不上",且无编译/测试报错——
/// 本测试用反射按 PermissionCode.Build 同规则从控制器算码,锁死漂移。一颗按钮可挂多条码,比对前先按 Split 展开。
/// </summary>
public class PermissionCodeConsistencyTests
{
    /// <summary>种子里每条码都对应一个真实存在、挂了 [RolePermission] 的内置端点。</summary>
    [Fact]
    public void Every_seeded_permission_code_maps_to_a_real_endpoint()
    {
        var endpointCodes = BuiltInEndpointCodes();
        var seededCodes = SeededPermissionCodes();

        Assert.NotEmpty(seededCodes);   // 防呆:种子真被读到
        foreach (var code in seededCodes)
            Assert.Contains(code, endpointCodes);
    }

    /// <summary>
    /// 每个挂了 [RolePermission] 的内置端点都出现在某颗种子按钮里——否则它对普通用户<b>静默 403</b>且无人察觉。
    /// 新增受权端点必须同批进 DefaultMenuSeed:并入所属页面的查询/新增/更新/删除,或给它一颗独立按钮。
    /// </summary>
    [Fact]
    public void Every_permission_endpoint_is_seeded()
    {
        var missing = BuiltInEndpointCodes().Except(SeededPermissionCodes()).OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0,
            "以下受权端点没有任何种子按钮承载 —— 普通用户将静默 403:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>反射内置控制器,按 {大写Method}:/{小写路由模板} 生成所有 [RolePermission] 端点的权限码。</summary>
    private static HashSet<string> BuiltInEndpointCodes()
    {
        var codes = new HashSet<string>();
        var controllers = typeof(SmartAdminSetup).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var hasPermission = method.GetCustomAttribute<RolePermissionAttribute>() is not null
                    || controller.GetCustomAttribute<RolePermissionAttribute>() is not null;
                if (!hasPermission) continue;

                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = Combine(controllerRoute, http.Template);
                    foreach (var verb in http.HttpMethods)
                        codes.Add(PermissionCode.Build(verb, template));
                }
            }
        }
        return codes;
    }

    private static List<SysMenu> SeedRows() => new DefaultMenuSeed().HasData().ToList();

    /// <summary>DefaultMenuSeed 里全部权限码,多码按钮展开成单条。</summary>
    private static HashSet<string> SeededPermissionCodes() =>
        SeedRows().SelectMany(m => PermissionCode.Split(m.Permission)).ToHashSet();

    private static string Combine(string controllerRoute, string? actionTemplate)
    {
        if (string.IsNullOrEmpty(actionTemplate)) return controllerRoute;
        // ASP.NET Core 以 / 或 ~/ 起始的动作模板是绝对路由，不能再拼控制器前缀。
        if (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal))
            return actionTemplate.TrimStart('~');
        return $"{controllerRoute.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";
    }
}
