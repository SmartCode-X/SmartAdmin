using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 高敏权限码集合与实际挂了 <c>[RequireReauth]</c> 的端点必须一一对应。
/// <para>两处是分离的手写串:特性挂在控制器上,码写在 <see cref="HighSensitivityPermissions.Default"/> 里。
/// 漏一处的后果是静默的——挂了特性却没进集合,则该端点在"高敏权限"治理页上看不见;进了集合却没挂特性,
/// 则治理页显示它受保护而实际不要求再认证。</para>
/// </summary>
public class HighSensitivityConsistencyTests
{
    [Fact]
    public void Every_reauth_endpoint_is_declared_high_sensitive()
    {
        var missing = ReauthEndpointCodes().Except(HighSensitivityPermissions.Default).Order().ToList();
        Assert.True(missing.Count == 0,
            "以下端点挂了 [RequireReauth] 却不在 HighSensitivityPermissions.Default:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_declared_high_sensitive_code_has_a_reauth_endpoint()
    {
        var stale = HighSensitivityPermissions.Default.Except(ReauthEndpointCodes()).Order().ToList();
        Assert.True(stale.Count == 0,
            "以下高敏码没有对应的 [RequireReauth] 端点(路由改了或端点删了?):\n  " + string.Join("\n  ", stale));
    }

    /// <summary>反射内置控制器,算出所有挂了 [RequireReauth] 的端点权限码(与授权管道同一条 Build 规则)。</summary>
    private static HashSet<string> ReauthEndpointCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var controllers = typeof(SmartAdminSetup).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            var controllerHasReauth = controller.GetCustomAttribute<RequireReauthAttribute>() is not null;

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!controllerHasReauth && method.GetCustomAttribute<RequireReauthAttribute>() is null) continue;

                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = string.IsNullOrEmpty(http.Template)
                        ? controllerRoute
                        : $"{controllerRoute.TrimEnd('/')}/{http.Template.TrimStart('/')}";
                    foreach (var verb in http.HttpMethods)
                        codes.Add(PermissionCode.Build(verb, template));
                }
            }
        }
        return codes;
    }
}
