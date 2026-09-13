using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.Auth.WeCom;

/// <summary>
/// 企业微信登录装配(可选包入口)。在 <c>AddSmartAdmin()</c> <b>之前</b>调用即把企业微信 provider
/// 并入 <see cref="IExternalAuthProvider"/> 集合(按 <see cref="WeComAuthOptions.Code"/> 选型,与内置 OIDC / 钉钉并存)。
/// </summary>
public static class WeComSetup
{
    /// <summary>
    /// 按配置启用企业微信登录:读 <c>SmartAdmin:ExternalAuth:WeCom</c> 节;未配 CorpId 则空操作(不点亮入口)。
    /// </summary>
    public static IServiceCollection AddSmartAdminWeComAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("SmartAdmin:ExternalAuth:WeCom").Get<WeComAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.CorpId))
            return services;
        return services.AddSmartAdminWeComAuth(options);
    }

    /// <summary>显式用给定配置启用企业微信登录(代码侧,不看配置节)。</summary>
    public static IServiceCollection AddSmartAdminWeComAuth(this IServiceCollection services, WeComAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CorpId)
            || string.IsNullOrWhiteSpace(options.CorpSecret)
            || string.IsNullOrWhiteSpace(options.AgentId))
            throw new InvalidOperationException(
                "启用企业微信登录需配置 CorpId + AgentId + CorpSecret(SmartAdmin:ExternalAuth:WeCom)。");

        services.AddSingleton(options);
        services.AddHttpClient(WeComExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));

        // 与 GitHub 相同:TryAddEnumerable + TImplementation,工厂注入命名 HttpClient
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, WeComExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(WeComExternalAuthProvider.HttpClientName);
            return new WeComExternalAuthProvider(
                sp.GetRequiredService<WeComAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<WeComExternalAuthProvider>>());
        }));
        return services;
    }
}
