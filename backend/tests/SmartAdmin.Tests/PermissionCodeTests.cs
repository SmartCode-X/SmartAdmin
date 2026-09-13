using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// <see cref="PermissionCode"/> 的拆合与规范化:一颗按钮挂多条码时,存储形态(<c>;</c> 连接)与授权比对用的单条码之间
/// 必须可逆、去重、且大小写差异到不了库里——否则"授了权、点了 403"会从手敲的码里溜回来。
/// </summary>
public class PermissionCodeTests
{
    [Fact]
    public void Build_uppercases_method_and_lowercases_template()
    {
        Assert.Equal("GET:/api/v1/ping", PermissionCode.Build("get", "/API/v1/Ping"));
        Assert.Equal("GET:/api/v1/ping", PermissionCode.Build("GET", "api/v1/ping"));
        Assert.Equal("GET:/", PermissionCode.Build("get", null));
    }

    [Fact]
    public void Normalize_rebuilds_from_first_colon_and_keeps_route_constraints()
    {
        Assert.Equal("GET:/api/v1/ping", PermissionCode.Normalize("get:/API/v1/Ping"));
        Assert.Equal("DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}",
            PermissionCode.Normalize("delete:/api/v1/sys/mfa/high-sensitivity/{id:long}"));
        Assert.Equal("not-a-code", PermissionCode.Normalize("not-a-code"));   // 没有冒号:原样(不会匹配任何端点)
    }

    [Fact]
    public void Split_trims_normalizes_dedups_and_keeps_order()
    {
        var codes = PermissionCode.Split(" get:/api/v1/sys/user/page ; GET:/api/v1/sys/user/{id};; GET:/api/v1/sys/user/page ");

        Assert.Equal(["GET:/api/v1/sys/user/page", "GET:/api/v1/sys/user/{id}"], codes);
    }

    [Fact]
    public void Split_of_blank_is_empty()
    {
        Assert.Empty(PermissionCode.Split(null));
        Assert.Empty(PermissionCode.Split(""));
        Assert.Empty(PermissionCode.Split(" ; "));
    }

    [Fact]
    public void Join_is_the_inverse_of_split()
    {
        const string stored = "GET:/api/v1/sys/user/page;GET:/api/v1/sys/user/{id}";

        Assert.Equal(stored, PermissionCode.Join(PermissionCode.Split(stored)));
        Assert.Equal("", PermissionCode.Join([]));
    }
}
