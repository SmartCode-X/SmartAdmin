using System.Net;
using System.Net.Http.Headers;
using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 一颗按钮挂多条权限码的端到端契约:角色勾上「用户-查询」即拿到列表与详情、拿不到写接口;
/// 菜单保存时码被归一化去重;运行时新建的多码按钮与种子按钮同样生效。
/// 授权判定本身仍逐条路由比对(<c>RolePermissionAttribute</c> 一字未动),只是一个勾选框展开成多条路由。
/// </summary>
public class MultiCodeMenuTests
{
    private const long UserQueryButtonId = 231;   // DefaultMenuSeed:用户-查询
    private const long OpsCatalogId = 300;        // DefaultMenuSeed:系统运维目录(运行时建的按钮挂这里)

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", AdminAppFactory.DefaultAdminPassword));
        return c;
    }

    /// <summary>建一个只授 <paramref name="menuIds"/> 的角色 + 挂它的用户,返回该用户的客户端与 Id。</summary>
    private static async Task<(HttpClient Client, long UserId)> UserWithMenus(AdminAppFactory f, HttpClient admin, string tag, params long[] menuIds)
    {
        var roleId = (await (await admin.PostJson("/api/v1/sys/role/add",
            new { name = $"多码-{tag}", code = $"multi-code-{tag}", sort = 0, enabled = true })).ReadEnvelope())
            .GetProperty("data").GetInt64();
        await admin.PutJson("/api/v1/sys/role/menu", new { roleId, menuIds });

        var userId = (await (await admin.PostJson("/api/v1/sys/user",
            new { account = $"mc-{tag}", password = "InitPass123", name = $"多码-{tag}", enabled = true, roleIds = new[] { roleId } }))
            .ReadEnvelope()).GetProperty("data").GetProperty("id").GetInt64();

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken($"mc-{tag}", "InitPass123"));
        return (c, userId);
    }

    [Fact]
    public async Task Query_button_grants_page_and_detail_but_nothing_else()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);
        var (user, userId) = await UserWithMenus(f, admin, "query", UserQueryButtonId);

        // 一个勾选框 → 列表、详情都通
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/sys/user/page?Current=1&Size=10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/v1/sys/user/{userId}")).StatusCode);

        // 同页的写接口、别页的读接口都不在这颗按钮里 → 403
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutJson($"/api/v1/sys/user/{userId}", new { name = "改名", enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/sys/role/page?Current=1&Size=10")).StatusCode);

        // 前端拿到的权限码集合是展开后的单条码,v-auth 仍按单条路由门控
        var codes = (await (await user.GetAsync("/api/v1/personal/permissions")).ReadEnvelope())
            .GetProperty("data").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("GET:/api/v1/sys/user/page", codes);
        Assert.Contains("GET:/api/v1/sys/user/{id}", codes);
        Assert.DoesNotContain("PUT:/api/v1/sys/user/{id}", codes);
        Assert.DoesNotContain(codes, c => c!.Contains(PermissionCode.Separator));
    }

    [Fact]
    public async Task Saving_a_button_normalizes_and_dedups_its_codes()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var id = (await (await admin.PostJson("/api/v1/sys/menu/add", new
        {
            parentId = OpsCatalogId, type = 3, title = "归一化", sort = 1, enabled = true,
            permission = " get:/api/v1/ping ; GET:/api/v1/ping;get:/API/v1/sys/user/page; ",
        })).ReadEnvelope()).GetProperty("data").GetInt64();

        var tree = (await (await admin.GetAsync("/api/v1/sys/menu/tree")).ReadEnvelope()).GetProperty("data");
        Assert.Equal("GET:/api/v1/ping;GET:/api/v1/sys/user/page", FindPermission(tree, id));
    }

    [Fact]
    public async Task Runtime_multi_code_button_grants_every_route_on_it()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdminClient(f);

        var buttonId = (await (await admin.PostJson("/api/v1/sys/menu/add", new
        {
            parentId = OpsCatalogId, type = 3, title = "探针+用户列表", sort = 1, enabled = true,
            permission = "GET:/api/v1/ping;GET:/api/v1/sys/user/page",
        })).ReadEnvelope()).GetProperty("data").GetInt64();

        var (user, _) = await UserWithMenus(f, admin, "runtime", buttonId);

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/sys/user/page?Current=1&Size=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/sys/role/page?Current=1&Size=10")).StatusCode);
    }

    /// <summary>在管理端菜单树里按 Id 找节点的 permission 字段(深度优先)。</summary>
    private static string? FindPermission(System.Text.Json.JsonElement nodes, long id)
    {
        foreach (var n in nodes.EnumerateArray())
        {
            if (n.GetProperty("id").GetInt64() == id) return n.GetProperty("permission").GetString();
            if (n.TryGetProperty("children", out var children) && children.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var found = FindPermission(children, id);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
