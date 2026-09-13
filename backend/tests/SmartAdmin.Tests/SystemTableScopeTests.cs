using System.Net.Http.Headers;
using SqlSugar;
using Microsoft.Extensions.DependencyInjection;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 系统表(<c>Sys*</c>)按数据范围收敛。这些表继承 <c>BaseEntity</c> 而非 <c>DataEntity</c>,
/// 全局过滤器对它们一行都不生效——可见性只能各服务显式收口;操作日志、在线会话、通知管理列表这几处一旦漏收口:
/// 一个被授了日志查看权的子管理员,能读到全公司的操作记录(含入参 JSON),还能强退任何人(包括超管)。
/// </summary>
public class SystemTableScopeTests
{
    private const string PASSWORD = "Scope@123456";

    /// <summary>造一个"本机构"范围、并授了若干路由权限的管理员。</summary>
    private static async Task<string> SeedScopedAdminAsync(AdminAppFactory f, long orgId, params string[] permissions)
    {
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var menus = sp.GetRequiredService<IRepository<SysMenu>>();
        var menuIds = new List<long>();
        foreach (var permission in permissions)
        {
            var menu = new SysMenu
            {
                ParentId = 1, Type = MenuType.Button, Title = permission,
                Permission = permission, Enabled = true, Visible = true,
            };
            await menus.InsertAsync(menu);
            menuIds.Add(menu.Id);
        }

        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var rbac = sp.GetRequiredService<IRbacService>();
        var role = new SysRole { Name = "范围管理员", Code = "st-" + Guid.CreateVersion7().ToString("N")[..8], Enabled = true };
        await roles.InsertAsync(role);
        if (menuIds.Count > 0) await rbac.SetRoleMenusAsync(role.Id, menuIds);
        await rbac.SetRoleDataScopeAsync(role.Id, DataScopeType.Org);

        var account = "st-" + Guid.CreateVersion7().ToString("N")[..8];
        await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
        {
            Account = account, Password = PASSWORD, Name = "范围管理员", Enabled = true, OrgId = orgId, RoleIds = [role.Id],
        });
        return account;
    }

    private static async Task<HttpClient> LoginAsync(AdminAppFactory f, string account, string password)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, password));
        return c;
    }

    /// <summary>操作日志:超管的操作记录不该出现在范围管理员的列表里。</summary>
    [Fact]
    public async Task Operation_log_only_shows_in_scope_operators()
    {
        using var f = new AdminAppFactory();
        var account = await SeedScopedAdminAsync(f, 930001, "GET:/api/v1/sys/log/op/page");

        // 超管做一次写操作,留下一条属于他自己的操作日志
        var admin = await LoginAsync(f, "superAdmin", "Test@123456");
        await admin.PostJson("/api/v1/sys/role/add", new { name = "超管建的角色", code = "sa-" + Guid.CreateVersion7().ToString("N")[..8], enabled = true });

        var scoped = await LoginAsync(f, account, PASSWORD);
        var env = await (await scoped.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope();
        var operators = env.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("operatorId").GetInt64()).Distinct().ToList();

        // 超管(Id=1)不在本机构范围内 → 他的记录不可见
        Assert.DoesNotContain(1L, operators);

        // 超管自己仍看得到(不受限)
        var adminEnv = await (await admin.GetAsync("/api/v1/sys/log/op/page?Current=1&Size=100")).ReadEnvelope();
        Assert.Contains(1L, adminEnv.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("operatorId").GetInt64()));
    }

    /// <summary>在线会话:范围管理员看不到超管的会话。</summary>
    [Fact]
    public async Task Online_sessions_only_show_in_scope_users()
    {
        using var f = new AdminAppFactory();
        var account = await SeedScopedAdminAsync(f, 930011, "GET:/api/v1/sys/session/online");

        _ = await LoginAsync(f, "superAdmin", "Test@123456");   // 超管建一个会话
        var scoped = await LoginAsync(f, account, PASSWORD);

        var env = await (await scoped.GetAsync("/api/v1/sys/session/online?Current=1&Size=100")).ReadEnvelope();
        var accounts = env.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("account").GetString()).ToList();

        Assert.DoesNotContain("superAdmin", accounts);
        Assert.Contains(account, accounts);   // 自己的会话看得到
    }

    /// <summary>强退:范围管理员踢不动超管的会话(回 42024,与"会话不存在"同码,不泄漏会话是否有效)。</summary>
    [Fact]
    public async Task Force_logout_cannot_kick_super_admin()
    {
        using var f = new AdminAppFactory();
        var account = await SeedScopedAdminAsync(f, 930021,
            "GET:/api/v1/sys/session/online", "DELETE:/api/v1/sys/session/{sessionid}");

        var admin = await LoginAsync(f, "superAdmin", "Test@123456");
        var adminSessionId = await SessionIdOfAsync(f, "superAdmin");

        var scoped = await LoginAsync(f, account, PASSWORD);
        var env = await (await scoped.DeleteAsync($"/api/v1/sys/session/{adminSessionId}")).ReadEnvelope();

        Assert.Equal((int)ErrorCode.SessionNotFound, env.GetProperty("code").GetInt32());
        // 超管会话仍然可用
        Assert.Equal(0, (await (await admin.GetAsync("/api/v1/ping")).ReadEnvelope()).GetProperty("code").GetInt32());
    }

    /// <summary>超管强退别人不受影响。</summary>
    [Fact]
    public async Task Super_admin_can_still_force_logout_anyone()
    {
        using var f = new AdminAppFactory();
        var account = await SeedScopedAdminAsync(f, 930031);
        var victim = await LoginAsync(f, account, PASSWORD);
        var victimSessionId = await SessionIdOfAsync(f, account);

        var admin = await LoginAsync(f, "superAdmin", "Test@123456");
        var env = await (await admin.DeleteAsync($"/api/v1/sys/session/{victimSessionId}")).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());

        // 被踢的会话下次请求即 401 信封
        var after = await (await victim.GetAsync("/api/v1/ping")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.TokenInvalid, after.GetProperty("code").GetInt32());
    }

    private static async Task<string> SessionIdOfAsync(AdminAppFactory f, string account)
    {
        using var scope = f.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRepository<SysSession>>();
        var row = await sessions.AsQueryable()
            .Where(s => s.Account == account && s.RevokedAt == null)
            .OrderBy(s => s.Id, OrderByType.Desc)
            .FirstAsync();
        return row.SessionId;
    }
}
