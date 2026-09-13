using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// Cookie 会话 + CSRF + 会话绝对窗与闲置超时。
/// </summary>
public class CookieSessionCsrfTests
{
    private static string DataProtectionKey => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Cookie 会话工厂:开 CookieMode,显式给绝对窗与闲置档(两者都没有内置下限,不配即不启用)。</summary>
    private static AdminAppFactory CookieSessionFactory(Action<IServiceCollection>? extra = null) => new()
    {
        Settings = new Dictionary<string, string?>
        {
            ["SmartAdmin:Security:Session:CookieMode"] = "true",
            ["SmartAdmin:Security:Session:AbsoluteHours"] = "8",
            ["SmartAdmin:Security:Session:IdleMinutesMfa"] = "15",
            ["SmartAdmin:Security:Session:IdleMinutesNormal"] = "30",
            ["SmartAdmin:Security:DataProtection:Key"] = DataProtectionKey,
            // 会话 TTL 显式给足,便于绝对窗测试用 FakeTime 推进
            ["SmartAdmin:Jwt:ExpireMinutes"] = "15",
            ["SmartAdmin:Jwt:RefreshExpireMinutes"] = "480",
        },
        Overrides = services =>
        {
            // 会话/Cookie 用例专注会话层:关闭 MFA 强制,避免超管未绑 TOTP 挡登录
            services.RemoveAll<IMfaPolicyService>();
            services.AddSingleton<IMfaPolicyService, NoMfaPolicy>();
            extra?.Invoke(services);
        },
    };

    private sealed class NoMfaPolicy : IMfaPolicyService
    {
        public Task<bool> IsTotpFeatureEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<bool> HoldsHighSensitivityPermissionAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static (string? Rt, string? Csrf) ParseAuthCookies(HttpResponseMessage resp)
    {
        string? rt = null, csrf = null;
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values))
            return (null, null);
        foreach (var raw in values)
        {
            if (raw.StartsWith(AuthCookieNames.RefreshToken + "=", StringComparison.OrdinalIgnoreCase))
                rt = CookieValue(raw, AuthCookieNames.RefreshToken);
            else if (raw.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase))
                csrf = CookieValue(raw, AuthCookieNames.Csrf);
        }
        return (rt, csrf);
    }

    private static string? CookieValue(string setCookie, string name)
    {
        var m = Regex.Match(setCookie, $"^{Regex.Escape(name)}=([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }

    private static bool SetCookieHasFlags(HttpResponseMessage resp, string cookieName, params string[] flags)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values)) return false;
        var line = values.FirstOrDefault(v => v.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase));
        if (line is null) return false;
        return flags.All(f => line.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cookie_mode_login_puts_refresh_in_httponly_cookie_not_body()
    {
        using var f = CookieSessionFactory();
        var c = f.CreateClient();
        var resp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        var data = j.GetProperty("data");
        Assert.False(string.IsNullOrEmpty(data.GetProperty("accessToken").GetString()));
        // body 清空 refresh
        Assert.True(
            !data.TryGetProperty("refreshToken", out var rtEl)
            || string.IsNullOrEmpty(rtEl.GetString()));

        Assert.True(SetCookieHasFlags(resp, AuthCookieNames.RefreshToken, "httponly", "secure", "samesite=lax"));
        Assert.True(SetCookieHasFlags(resp, AuthCookieNames.Csrf, "secure", "samesite=lax"));
        // CSRF 可读(无 HttpOnly)
        if (resp.Headers.TryGetValues("Set-Cookie", out var vals))
        {
            var csrfLine = vals.First(v => v.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("httponly", csrfLine, StringComparison.OrdinalIgnoreCase);
        }

        var (rt, csrf) = ParseAuthCookies(resp);
        Assert.False(string.IsNullOrEmpty(rt));
        Assert.False(string.IsNullOrEmpty(csrf));
    }

    [Fact]
    public async Task Default_mode_login_keeps_body_refresh_without_auth_cookies()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        var resp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("refreshToken").GetString()));

        if (resp.Headers.TryGetValues("Set-Cookie", out var vals))
        {
            Assert.DoesNotContain(vals, v => v.StartsWith(AuthCookieNames.RefreshToken + "=", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(vals, v => v.StartsWith(AuthCookieNames.Csrf + "=", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Csrf_rejects_without_header_and_passes_when_matched()
    {
        using var f = CookieSessionFactory();
        var c = f.CreateClient();
        var loginResp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var login = await loginResp.ReadEnvelope();
        Assert.Equal(0, login.GetProperty("code").GetInt32());
        var (rt, csrf) = ParseAuthCookies(loginResp);
        Assert.False(string.IsNullOrEmpty(rt));
        Assert.False(string.IsNullOrEmpty(csrf));
        var access = login.GetProperty("data").GetProperty("accessToken").GetString()!;

        // 无 CSRF 头 + 带 refresh cookie → 写操作 403/40023
        var bad = f.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        bad.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        var denied = await bad.PostJson("/api/v1/auth/logout", new { });
        var deniedBody = await denied.ReadEnvelope();
        Assert.Equal((int)ErrorCode.CsrfInvalid, deniedBody.GetProperty("code").GetInt32());

        // TOTP 完成登录同样是状态改变 POST:有 refresh Cookie 时缺 CSRF 必须拒(防 raw fetch 绕过中间件)
        var totpNoCsrf = f.CreateClient();
        totpNoCsrf.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        var totpDenied = await totpNoCsrf.PostJson("/api/v1/auth/login/totp",
            new { challengeId = "x", code = "000000" });
        var totpDeniedBody = await totpDenied.ReadEnvelope();
        Assert.Equal((int)ErrorCode.CsrfInvalid, totpDeniedBody.GetProperty("code").GetInt32());

        // 匹配 CSRF → 通过
        var good = f.CreateClient();
        good.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        good.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        good.DefaultRequestHeaders.TryAddWithoutValidation(AuthCookieNames.CsrfHeader, csrf);
        var ok = await (await good.PostJson("/api/v1/auth/logout", new { })).ReadEnvelope();
        Assert.Equal(0, ok.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Clear_cookies_reuse_CookieDomain_and_samesite_none()
    {
        // HttpClient CookieContainer 不接受 domain=.example.com 于 localhost URI,故用 HttpContext 直接断言
        var security = new AdminSecurityOptions
        {
            Session = new AdminSessionOptions { CookieMode = true, CookieDomain = ".example.com" },
        };
        var cookies = new AuthCookieService(security, new StubHostEnv());
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        cookies.ClearAuthCookies(http);

        Assert.True(http.Response.Headers.TryGetValue("Set-Cookie", out var setCookies));
        var lines = setCookies.ToString();
        // 删除响应须带与创建时相同的 Domain / Secure / SameSite
        Assert.Contains("domain=.example.com", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AuthCookieNames.RefreshToken, lines, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AuthCookieNames.Csrf, lines, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHostEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task Refresh_reads_cookie_when_body_is_empty()
    {
        using var f = CookieSessionFactory();
        var c = f.CreateClient();
        var loginResp = await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });
        var login = await loginResp.ReadEnvelope();
        var (rt, csrf) = ParseAuthCookies(loginResp);
        Assert.False(string.IsNullOrEmpty(rt));

        var refreshClient = f.CreateClient();
        refreshClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",
            $"{AuthCookieNames.RefreshToken}={rt}; {AuthCookieNames.Csrf}={csrf}");
        refreshClient.DefaultRequestHeaders.TryAddWithoutValidation(AuthCookieNames.CsrfHeader, csrf!);
        // body 空 refreshToken
        var resp = await refreshClient.PostJson("/api/v1/auth/refresh", new { });
        var j = await resp.ReadEnvelope();
        Assert.Equal(0, j.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("data").GetProperty("accessToken").GetString()));
        // 仍不在 body 下发 refresh
        var data = j.GetProperty("data");
        Assert.True(!data.TryGetProperty("refreshToken", out var rtEl) || string.IsNullOrEmpty(rtEl.GetString()));
        // 新 Cookie 轮换
        var (rt2, csrf2) = ParseAuthCookies(resp);
        Assert.False(string.IsNullOrEmpty(rt2));
        Assert.NotEqual(rt, rt2);
        Assert.False(string.IsNullOrEmpty(csrf2));
    }

    [Fact]
    public async Task Absolute_window_expires_the_session()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = CookieSessionFactory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

        // 直接驱动 SessionService:开会话 → 推进绝对窗 → IsActive false
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user);
        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(user!.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid, pair);

        var row = await sessionRepo.GetFirstAsync(s => s.SessionId == sid);
        Assert.NotNull(row);
        // 绝对窗 ≤ now+8h
        Assert.True(row!.AbsoluteExpiresAt <= clock.GetUtcNow().UtcDateTime.AddHours(8).AddSeconds(2));
        Assert.True(await sessions.IsActiveAsync(sid));

        // 推进超过绝对窗
        clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        Assert.False(await sessions.IsActiveAsync(sid));
    }

    [Fact]
    public async Task Idle_timeout_uses_the_configured_mfa_minutes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var f = CookieSessionFactory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenProvider>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        user!.TotpEnabled = true;
        await users.UpdateAsync(user);

        var sid = Guid.CreateVersion7().ToString("N");
        var pair = tokens.Create(
            new TokenSubject(user.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
            TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
        await sessions.OpenAsync(user, sid, pair);

        // 清缓存并回写 LastActivityAt 为 now,避免 IsActive 的 Touch 刷新活动时间干扰
        await cache.RemoveAsync(CacheKeys.Session(sid));
        var openAt = clock.GetUtcNow().UtcDateTime;
        await sessionRepo.Db.Updateable<SysSession>()
            .SetColumns(s => s.LastActivityAt == openAt)
            .Where(s => s.SessionId == sid)
            .ExecuteCommandAsync();

        // 推进 16 分钟(> MFA idle 15)且不经 IsActive Touch
        clock.Advance(TimeSpan.FromMinutes(16));
        await cache.RemoveAsync(CacheKeys.Session(sid));
        Assert.False(await sessions.IsActiveAsync(sid));
    }

    [Fact]
    public async Task Successful_login_updates_last_successful_login_at()
    {
        using var f = CookieSessionFactory();
        var c = f.CreateClient();
        await c.PostJson("/api/v1/auth/login", new { account = "superAdmin", password = "Test@123456" });

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user!.LastSuccessfulLoginAt);
    }
}

