using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.Auth.GitHub;

/// <summary>GitHub 登录装配:在 <c>AddSmartAdmin()</c> 之前调用。</summary>
public static class GitHubSetup
{
    /// <summary>从配置节 <c>SmartAdmin:ExternalAuth:GitHub</c> 读取选项并接入 GitHub 登录;未配置 ClientId 时静默跳过。</summary>
    public static IServiceCollection AddSmartAdminGitHubAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("SmartAdmin:ExternalAuth:GitHub").Get<GitHubAuthOptions>();
        if (options is null || string.IsNullOrWhiteSpace(options.ClientId))
            return services;
        return services.AddSmartAdminGitHubAuth(options);
    }

    /// <summary>用给定选项接入 GitHub 登录:注册命名 HttpClient 并挂载 <see cref="IExternalAuthProvider"/>。</summary>
    public static IServiceCollection AddSmartAdminGitHubAuth(this IServiceCollection services, GitHubAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("启用 GitHub 登录需配置 ClientId + ClientSecret(SmartAdmin:ExternalAuth:GitHub)。");

        services.AddSingleton(options);
        services.AddHttpClient(GitHubExternalAuthProvider.HttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(15);
                // GitHub API 要求有效 User-Agent;无密钥
                GitHubExternalAuthProvider.EnsureDefaultUserAgent(c);
            });

        // 必须带 TImplementation=GitHubExternalAuthProvider:TryAddEnumerable 用 impl 类型去重;
        // 仅 Singleton<IExternalAuthProvider>(factory) 会把 impl 当成接口本身 → ArgumentException。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalAuthProvider, GitHubExternalAuthProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(GitHubExternalAuthProvider.HttpClientName);
            return new GitHubExternalAuthProvider(
                sp.GetRequiredService<GitHubAuthOptions>(),
                http,
                sp.GetRequiredService<ILogger<GitHubExternalAuthProvider>>());
        }));
        return services;
    }
}
