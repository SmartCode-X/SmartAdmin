using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.Auth.WeChat;

/// <summary>微信登录装配:在 <c>AddSmartAdmin()</c> 之前调用。</summary>
public static class WeChatSetup
{
    /// <summary>从配置节 <c>SmartAdmin:ExternalAuth:WeChat</c> 读取选项并接入微信登录;未配置 AppId 时静默跳过。</summary>
    public static IServiceCollection AddSmartAdminWeChatAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("SmartAdmin:ExternalAuth:WeChat").Get<WeChatAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.AppId))
            return services;
        return services.AddSmartAdminWeChatAuth(options);
    }

    /// <summary>用给定选项接入微信登录:注册命名 HttpClient 并挂载 <see cref="IExternalAuthProvider"/>。</summary>
    public static IServiceCollection AddSmartAdminWeChatAuth(this IServiceCollection services, WeChatAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AppId) || string.IsNullOrWhiteSpace(options.AppSecret))
            throw new InvalidOperationException("启用微信登录需配置 AppId + AppSecret(SmartAdmin:ExternalAuth:WeChat)。");

        services.AddSingleton(options);
        services.AddHttpClient(WeChatExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));

        // 必须带 TImplementation=WeChatExternalAuthProvider(同 WeCom TryAddEnumerable 成法);
        // 仅 Singleton<IExternalAuthProvider>(factory) → ArgumentException,装包后 0 个 provider。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, WeChatExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(WeChatExternalAuthProvider.HttpClientName);
            return new WeChatExternalAuthProvider(
                sp.GetRequiredService<WeChatAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<WeChatExternalAuthProvider>>());
        }));
        return services;
    }
}
