using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SmartAdmin.TestHost;

namespace SmartAdmin.Tests;

/// <summary>
/// 数据范围的两条新不变量:只挂 <c>[Authorize]</c> 的端点也按真实范围过滤;未绑定时的回退是空范围而非看全库。
/// <para>若没有这两条不变量,消费者自建控制器只写 <c>[Authorize]</c> 就可能看到全部机构的数据,且没有任何报错——
/// 这是招牌能力上最危险的一个默认值。</para>
/// </summary>
public class DataScopeFailClosedTests
{
    /// <summary>造一个"本机构范围"的普通用户,并在两个机构各插一行 doc;返回登录信息。</summary>
    private static async Task<(string Account, string Password)> SeedScopedUserAsync(
        AdminAppFactory f, long orgA, long orgB)
    {
        const string password = "Scope@123456";
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var role = new SysRole { Name = "范围测试角色", Code = "fc-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
        await roles.InsertAsync(role);
        await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);   // 本机构

        var account = "fc-" + Guid.CreateVersion7().ToString("N")[..8];
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account, Password = password, Name = "机构A用户", Enabled = true, OrgId = orgA, RoleIds = [role.Id],
        });

        var docs = sp.GetRequiredService<IRepository<SampleDoc>>();
        await docs.InsertAsync(new SampleDoc { Title = "A 的文档", CreateOrgId = orgA });
        await docs.InsertAsync(new SampleDoc { Title = "B 的文档", CreateOrgId = orgB });

        return (account, password);
    }

    /// <summary>只挂 [Authorize] 的端点:全局自动绑定给出真实范围 → 只看得到本机构那一行。</summary>
    [Fact]
    public async Task Authorize_only_endpoint_is_filtered_by_real_scope()
    {
        using var f = new AdminAppFactory();
        var (account, password) = await SeedScopedUserAsync(f, 920001, 920002);

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));

        var data = (await (await c.GetAsync("/api/v1/sample/doc/authorize-only")).ReadEnvelope()).GetProperty("data");
        var titles = data.EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();

        Assert.Equal(["A 的文档"], titles);   // 不再看到 B 机构的行
    }

    /// <summary>超管不受影响:同一个端点仍看得到全部。</summary>
    [Fact]
    public async Task Authorize_only_endpoint_is_unrestricted_for_super_admin()
    {
        using var f = new AdminAppFactory();
        await SeedScopedUserAsync(f, 920011, 920012);

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var data = (await (await c.GetAsync("/api/v1/sample/doc/authorize-only")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    /// <summary>
    /// fail-open 开关<b>不会放宽已绑定的范围</b>:它只决定"绑都没绑上"时兜底往哪边倒。
    /// 写这条是为了防止把它误当成"数据范围总开关"——真要那个效果得改角色的范围配置。
    /// (回退语义本身由 <see cref="HttpContextDataScopeContextTests"/> 直接覆盖。)
    /// </summary>
    [Fact]
    public async Task Fail_open_switch_does_not_widen_a_bound_scope()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?> { ["SmartAdmin:Security:DataScopeFailOpen"] = "true" },
        };
        var (account, password) = await SeedScopedUserAsync(f, 920021, 920022);

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));

        var data = (await (await c.GetAsync("/api/v1/sample/doc/authorize-only")).ReadEnvelope()).GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
    }
}

/// <summary>
/// <see cref="HttpContextDataScopeContext"/> 回退语义的单测(不经 HTTP 管道,直接构造)。
/// </summary>
public class HttpContextDataScopeContextTests
{
    private static HttpContextDataScopeContext Make(HttpContext? ctx, bool failOpen = false) =>
        new(new StubAccessor(ctx), new AdminSecurityOptions { DataScopeFailOpen = failOpen });

    [Fact]
    public void No_http_context_is_unrestricted()   // 后台任务、启动期、种子
        => Assert.True(Make(null).Current.IsUnrestricted);

    [Fact]
    public void Anonymous_request_is_unrestricted()   // 登录页等匿名端点查的是非机构表
        => Assert.True(Make(new DefaultHttpContext()).Current.IsUnrestricted);

    [Fact]
    public void Super_admin_is_unrestricted()
        => Assert.True(Make(Authenticated(7, superAdmin: true)).Current.IsUnrestricted);

    [Fact]
    public void Authenticated_user_without_binding_gets_empty_scope()
    {
        var scope = Make(Authenticated(7)).Current;

        Assert.False(scope.IsUnrestricted);
        Assert.Empty(scope.OrgIds);
        Assert.True(scope.IncludeSelf);
        Assert.Equal(7, scope.UserId);
    }

    [Fact]
    public void Fail_open_option_restores_unrestricted_fallback()
        => Assert.True(Make(Authenticated(7), failOpen: true).Current.IsUnrestricted);

    [Fact]
    public void Explicitly_bound_scope_wins_over_fallback()
    {
        var ctx = Authenticated(7);
        var sut = Make(ctx);
        var bound = DataScopeResult.Restricted([42], includeSelf: false, 7);

        sut.Current = bound;

        Assert.Same(bound, sut.Current);
    }

    private static DefaultHttpContext Authenticated(long userId, bool superAdmin = false)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        if (superAdmin) claims.Add(new Claim(TokenClaimNames.SUPER_ADMIN, "true"));
        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }

    private sealed class StubAccessor(HttpContext? ctx) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => ctx; set => throw new NotSupportedException(); }
    }
}
