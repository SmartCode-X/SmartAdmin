using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 机器端接入:[ApiKey] 端点只认请求头里的 API Key,与用户 JWT 并存互不干扰。
/// 覆盖 TestHost DiagController 的四种形态(只挂 [ApiKey] / 叠 [RolePermission] / [SkipEnvelope] / 写端点留痕),
/// 以及"JWT 端点不认 key、key 端点不认 JWT"的隔离,和 IApiKeyValidator 的可替换。
/// </summary>
public class ApiKeyAuthTests
{
    private const string KEY_PDA = "k-pda-0123456789abcdef";
    private const string KEY_OTHER = "k-other-0123456789abcdef";

    /// <summary>配置节里两把 key(默认 ConfigApiKeyValidator 的数据源),都不绑用户。</summary>
    private static Dictionary<string, string?> TwoKeys() => new()
    {
        ["SmartAdmin:Security:ApiKey:Keys:0:Name"] = "pda",
        ["SmartAdmin:Security:ApiKey:Keys:0:Key"] = KEY_PDA,
        ["SmartAdmin:Security:ApiKey:Keys:1:Name"] = "other",
        ["SmartAdmin:Security:ApiKey:Keys:1:Key"] = KEY_OTHER,
    };

    private static HttpClient Machine(AdminAppFactory f, string? key, string header = "X-Api-Key")
    {
        var c = f.CreateClient();
        if (key is not null) c.DefaultRequestHeaders.Add(header, key);
        return c;
    }

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    [Fact]
    public async Task No_key_and_wrong_key_get_401_with_api_key_code()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        var none = await Machine(f, null).GetAsync("/api/v1/diag/machine");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(40027, (await none.ReadEnvelope()).GetProperty("code").GetInt32());   // 不是 JWT 的 40006

        var wrong = await Machine(f, "k-nope").GetAsync("/api/v1/diag/machine");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(40027, (await wrong.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Right_key_is_authenticated_as_the_key_name()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        var ok = await Machine(f, KEY_PDA).GetAsync("/api/v1/diag/machine");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var env = await ok.ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        Assert.Equal("pda", env.GetProperty("data").GetString());   // unique_name = key 的名字
    }

    /// <summary>头名可配:改成 X-Device-Token 后默认头不再被认。</summary>
    [Fact]
    public async Task Header_name_is_configurable()
    {
        var settings = TwoKeys();
        settings["SmartAdmin:Security:ApiKey:HeaderName"] = "X-Device-Token";
        using var f = new AdminAppFactory { Settings = settings };

        Assert.Equal(HttpStatusCode.Unauthorized, (await Machine(f, KEY_PDA).GetAsync("/api/v1/diag/machine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Machine(f, KEY_PDA, "X-Device-Token").GetAsync("/api/v1/diag/machine")).StatusCode);
    }

    /// <summary>两个 scheme 互不干扰:JWT 打不开 [ApiKey] 端点,key 也打不开 JWT 端点(那边照旧 40006)。</summary>
    [Fact]
    public async Task Jwt_and_api_key_do_not_cross_over()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        var jwt = await SuperAdminClient(f);
        var viaJwt = await jwt.GetAsync("/api/v1/diag/machine");
        Assert.Equal(HttpStatusCode.Unauthorized, viaJwt.StatusCode);
        Assert.Equal(40027, (await viaJwt.ReadEnvelope()).GetProperty("code").GetInt32());

        var viaKey = await Machine(f, KEY_PDA).GetAsync("/api/v1/personal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, viaKey.StatusCode);
        Assert.Equal(40006, (await viaKey.ReadEnvelope()).GetProperty("code").GetInt32());

        // JWT 路径本身一切照旧
        Assert.Equal(HttpStatusCode.OK, (await jwt.GetAsync("/api/v1/personal/profile")).StatusCode);
    }

    /// <summary>未绑定用户的 key 叠 [RolePermission]:没有 sub 就没有任何权限 → 403,而不是 500。</summary>
    [Fact]
    public async Task Unbound_key_on_permission_endpoint_is_forbidden_not_500()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        var res = await Machine(f, KEY_PDA).GetAsync("/api/v1/diag/machine-perm");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(41001, (await res.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    /// <summary>
    /// 绑定了用户的 key 叠 [RolePermission]:按该用户的角色判——授了码 200、没授 403。
    /// 用户 Id 是运行时雪花号,配置节写不了,所以这里换一个 IApiKeyValidator(顺带证明它可替换)。
    /// </summary>
    [Fact]
    public async Task Bound_key_reuses_the_users_rbac()
    {
        var bound = new BoundValidator();
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IApiKeyValidator>(bound)),
        };
        Assert.Same(bound, f.Services.GetRequiredService<IApiKeyValidator>());

        // 建目录 + 权限按钮(GET:/api/v1/diag/machine-perm)+ 角色 + 用户,把 key 绑到这个用户
        var admin = await SuperAdminClient(f);
        var groupId = (await (await admin.PostJson("/api/v1/sys/menu/add",
            new { parentId = 0, type = 1, title = "设备接口", permission = "", sort = 99, enabled = true, moduleId = 1, visible = false }))
            .ReadEnvelope()).GetProperty("data").GetInt64();
        var buttonId = (await (await admin.PostJson("/api/v1/sys/menu/add",
            new { parentId = groupId, type = 3, title = "机器权限", permission = "GET:/api/v1/diag/machine-perm", sort = 1, enabled = true }))
            .ReadEnvelope()).GetProperty("data").GetInt64();
        var roleId = (await (await admin.PostJson("/api/v1/sys/role/add",
            new { name = "设备", code = "device", sort = 0, enabled = true })).ReadEnvelope()).GetProperty("data").GetInt64();
        var userId = (await (await admin.PostJson("/api/v1/sys/user",
            new { account = "device-1", password = "Test@123456", name = "设备一号", enabled = true, roleIds = new[] { roleId } }))
            .ReadEnvelope()).GetProperty("data").GetProperty("id").GetInt64();
        bound.UserId = userId;

        var machine = Machine(f, KEY_PDA);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.GetAsync("/api/v1/diag/machine-perm")).StatusCode);   // 还没授

        await admin.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds = new[] { buttonId } });
        var ok = await machine.GetAsync("/api/v1/diag/machine-perm");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("perm", (await ok.ReadEnvelope()).GetProperty("data").GetString());

        // 收回授权即 403:key 绑了用户 ≠ 万能钥匙,授权变更对机器主体同样即时生效
        await admin.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds = Array.Empty<long>() });
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.GetAsync("/api/v1/diag/machine-perm")).StatusCode);

        // 没挂 [ApiKey] 的用户端点仍只认 JWT:key 打过去是 401(40006),不是 403
        var userOnly = await machine.GetAsync("/api/v1/sys/user/page?Current=1&Size=10");
        Assert.Equal(HttpStatusCode.Unauthorized, userOnly.StatusCode);
        Assert.Equal(40006, (await userOnly.ReadEnvelope()).GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Skip_envelope_returns_the_bare_dto()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        var res = await Machine(f, KEY_PDA).GetAsync("/api/v1/diag/machine-raw");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("raw", doc.RootElement.GetProperty("name").GetString());   // 顶层就是 dto
        Assert.False(doc.RootElement.TryGetProperty("code", out _));           // 没有信封外壳
    }

    /// <summary>机器写操作也要留痕:操作日志一条,操作人为空(未绑定用户),路径与方法照记。</summary>
    [Fact]
    public async Task Machine_write_is_recorded_in_operation_log()
    {
        using var f = new AdminAppFactory { Settings = TwoKeys() };

        Assert.Equal(HttpStatusCode.OK, (await Machine(f, KEY_PDA).PostJson("/api/v1/diag/machine-write", new { })).StatusCode);

        using var scope = f.Services.CreateScope();
        var logs = scope.ServiceProvider.GetRequiredService<IRepository<SysOpLog>>();
        var row = await logs.AsQueryable().Where(x => x.Path == "/api/v1/diag/machine-write").FirstAsync();
        Assert.NotNull(row);
        Assert.Equal("POST", row.HttpMethod);
        Assert.Null(row.OperatorId);
    }

    /// <summary>OpenAPI 契约把 [ApiKey] 端点标成 x-auth=apikey,[SkipEnvelope] 的 200 schema 不加信封外壳。</summary>
    [Fact]
    public async Task Contract_marks_api_key_endpoints_and_skips_envelope_schema()
    {
        using var f = new AdminAppFactory();
        var json = await f.CreateClient().GetStringAsync("/openapi/v1.json");
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");

        var machine = paths.GetProperty("/api/v1/diag/machine").GetProperty("get");
        Assert.Equal("apikey", machine.GetProperty("x-auth").GetString());

        var raw = paths.GetProperty("/api/v1/diag/machine-raw").GetProperty("get");
        var schema = raw.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.False(schema.TryGetProperty("properties", out var props) && props.TryGetProperty("code", out _));
    }

    /// <summary>可替换的假实现:固定认 KEY_PDA,绑到测试里现建的用户。</summary>
    private sealed class BoundValidator : IApiKeyValidator
    {
        public long? UserId { get; set; }

        public Task<ApiKeyPrincipal?> ValidateAsync(string apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(apiKey == KEY_PDA ? new ApiKeyPrincipal("pda", UserId) : null);
    }
}

/// <summary>机器端限流:按 key 分区、与用户端 IP 桶分开计数。阈值只来自 Options(KeyPermitPerWindow)。</summary>
public class ApiKeyRateLimitTests
{
    private const string KEY_A = "k-a-0123456789abcdef";
    private const string KEY_B = "k-b-0123456789abcdef";

    private static async Task<AdminAppFactory> EnabledAsync(int keyPermit)
    {
        var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                ["SmartAdmin:Security:RateLimit:Enabled"] = "true",
                ["SmartAdmin:Security:RateLimit:KeyPermitPerWindow"] = keyPermit.ToString(),
                ["SmartAdmin:Security:ApiKey:Keys:0:Name"] = "a",
                ["SmartAdmin:Security:ApiKey:Keys:0:Key"] = KEY_A,
                ["SmartAdmin:Security:ApiKey:Keys:1:Name"] = "b",
                ["SmartAdmin:Security:ApiKey:Keys:1:Key"] = KEY_B,
            },
            // 断 429 的用例必须冻结时钟:固定窗口跨边界计数归零(见 RateLimitTests 的注释)
            Overrides = s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
            },
        };
        // DB 里的总开关也要开(种子默认值可能是关的),与 RateLimitTests 同一套写法
        using var scope = f.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigService>();
        await config.SaveValuesAsync([new ConfigBatchItem { ConfigKey = AdminRateLimitOptions.KEY_ENABLED, ConfigValue = "True" }]);
        await f.Services.GetRequiredService<RuntimeRateLimit>().RefreshAsync();
        return f;
    }

    private static HttpClient WithKey(AdminAppFactory f, string key)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", key);
        return c;
    }

    [Fact]
    public async Task Keys_are_counted_separately_and_apart_from_the_ip_bucket()
    {
        using var f = await EnabledAsync(keyPermit: 2);
        var a = WithKey(f, KEY_A);
        var b = WithKey(f, KEY_B);

        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/diag/machine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/diag/machine")).StatusCode);
        var limited = await a.GetAsync("/api/v1/diag/machine");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(40008, (await limited.ReadEnvelope()).GetProperty("code").GetInt32());
        Assert.True(limited.Headers.Contains("Retry-After"));

        // 另一把 key 自己的额度,不受 A 影响
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/v1/diag/machine")).StatusCode);

        // 同一个 IP 上不带 key 的请求走用户端 IP 桶(默认 300/窗口),A 刷爆了也不殃及
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Zero_key_permit_means_unlimited()
    {
        using var f = await EnabledAsync(keyPermit: 0);
        var a = WithKey(f, KEY_A);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/diag/machine")).StatusCode);
    }
}
