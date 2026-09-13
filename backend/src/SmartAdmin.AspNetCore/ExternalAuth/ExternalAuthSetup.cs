using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>外部登录 provider 的内置装配。内核只装内置 OIDC provider;企业微信/钉钉等由可选包各自前置注册。</summary>
public static class ExternalAuthSetup
{
    /// <summary>
    /// 按 appsettings 的 <c>SmartAdmin:ExternalAuth:Oidc</c> 列表,每个条目注册一个 <see cref="OidcExternalAuthProvider"/> 实例。
    /// <para>用 plain <c>AddSingleton</c>(<b>非</b> <c>TryAddEnumerable</c>):多条 OIDC 都是同一 impl 类型、仅配置不同,
    /// 须保留 N 个实例(TryAddEnumerable 按 impl 类型去重会塌成 1)。消费者接自有 IdP:在 <c>AddSmartAdmin()</c> 前
    /// 自行注册 <see cref="IExternalAuthProvider"/>(用不同 Code),与内置并存,由 AuthService 按 Code 选型。</para>
    /// </summary>
    public static IServiceCollection AddExternalAuthProviders(this IServiceCollection services, AdminExternalAuthOptions options)
    {
        ValidateCallbackBaseUrl(options);

        if (options.Oidc.Count == 0)
            return services;

        services.AddHttpClient();   // 幂等(内部 TryAdd):确保 IHttpClientFactory 可用,token 端点交换用

        foreach (var oidc in options.Oidc)
            services.AddSingleton<IExternalAuthProvider>(sp => new OidcExternalAuthProvider(
                oidc,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<OidcExternalAuthProvider>>(),
                // 仅开发环境放行 http 元数据;生产强制 https(fail-closed,见 OidcExternalAuthProvider 构造)
                allowHttpMetadata: sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));

        return services;
    }

    /// <summary>
    /// <c>CallbackBaseUrl</c> 只填后端对外的根地址,回调路径由内核接在后面。填错了厂商照样跳转(它们只校验域名),
    /// 最后落到一条不存在的路径上,表现为「授权完什么也没发生」,所以启动时就拦下。
    /// 最容易填错的两种:整条回调地址,和前端结果页 <c>FrontendResultPath</c>。
    /// </summary>
    private static void ValidateCallbackBaseUrl(AdminExternalAuthOptions options)
    {
        var value = options.CallbackBaseUrl;
        if (string.IsNullOrWhiteSpace(value)) return;

        var valid = Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        if (valid)
        {
            var path = uri!.AbsolutePath.TrimEnd('/');
            var frontendPath = Uri.TryCreate(options.FrontendResultPath, UriKind.Absolute, out var frontend)
                ? frontend.AbsolutePath
                : options.FrontendResultPath.Split('?')[0];
            valid = !path.Contains("/api/v1/auth/external", StringComparison.OrdinalIgnoreCase)
                    && (path.Length == 0 || !string.Equals(path, frontendPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        if (!valid)
            throw new InvalidOperationException(
                $"SmartAdmin:ExternalAuth:CallbackBaseUrl 只填后端对外的根地址,如 https://admin.example.com,当前值:{value}。" +
                "回调路径 /api/v1/auth/external/{provider}/callback 由内核拼接,不要填整条回调地址;" +
                "登录完成后跳回的前端页面是 SmartAdmin:ExternalAuth:FrontendResultPath,不是这一项。");
    }
}
