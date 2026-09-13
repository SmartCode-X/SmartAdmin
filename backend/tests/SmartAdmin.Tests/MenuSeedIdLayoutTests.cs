using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 内置菜单种子的 Id 编号规则(见 <see cref="DefaultMenuSeed"/> 头注释):百位是分区,整百是目录,页面取整十,
/// 按钮与所属页面(或目录)同一个十位段;页面按钮的个位 1–4 依次是查询、新增、更新、删除。
/// </summary>
public class MenuSeedIdLayoutTests
{
    private static readonly List<SysMenu> Rows = new DefaultMenuSeed().HasData().ToList();

    [Fact]
    public void Ids_follow_block_page_slot_layout()
    {
        var byId = Rows.ToDictionary(m => m.Id);
        var errors = new List<string>();

        foreach (var m in Rows)
        {
            var parent = m.ParentId == 0 ? null : byId.GetValueOrDefault(m.ParentId);
            var ok = (m.Type, parent?.Type) switch
            {
                (MenuType.Catalog, null) => m.Id >= 200 && m.Id % 100 == 0,
                (MenuType.Menu, null) => m.Id is >= 100 and < 200 && m.Id % 10 == 0,   // 根级页面(工作台)在 1xx
                (MenuType.Menu, MenuType.Catalog) => m.Id % 10 == 0 && m.Id % 100 != 0 && m.Id / 100 == m.ParentId / 100,
                (MenuType.Button, MenuType.Menu or MenuType.Catalog) => m.Id % 10 != 0 && m.Id / 10 == m.ParentId / 10,
                _ => false,
            };
            if (!ok) errors.Add($"{m.Id} {m.Title}(ParentId={m.ParentId})");
        }

        Assert.True(errors.Count == 0, "以下节点不符合 Id 编号规则:\n  " + string.Join("\n  ", errors));
    }

    /// <summary>页面按钮个位 1–4 的语义用 HTTP 方法核对:查询全是 GET,新增全是 POST,更新全是 PUT,删除含 DELETE。</summary>
    [Fact]
    public void Standard_button_slots_match_http_methods()
    {
        var pages = Rows.Where(m => m.Type == MenuType.Menu).Select(m => m.Id).ToHashSet();
        var errors = new List<string>();

        foreach (var b in Rows.Where(m => m.Type == MenuType.Button && pages.Contains(m.ParentId)))
        {
            var methods = PermissionCode.Split(b.Permission).Select(c => c[..c.IndexOf(':')]).ToList();
            var ok = (b.Id % 10) switch
            {
                1 => methods.All(x => x == "GET"),
                2 => methods.All(x => x == "POST"),
                3 => methods.All(x => x == "PUT"),
                4 => methods.Contains("DELETE"),
                _ => true,
            };
            if (!ok) errors.Add($"{b.Id} {b.Title}: {b.Permission}");
        }

        Assert.True(errors.Count == 0, "以下按钮的个位与权限码的 HTTP 方法对不上:\n  " + string.Join("\n  ", errors));
    }
}
