using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 授权面一改,受影响用户的权限/范围缓存要一起失效。这件事本身没错,错的是<b>逐个删</b>:
/// 改一个角色可能牵动几千个用户,进程内缓存看不出差别,Redis 上那就是几千个串行往返。
/// </summary>
public class CacheBatchInvalidationTests
{
    private static AdminAppFactory Factory(CountingCacheProvider counter) => new()
    {
        Overrides = s =>
        {
            s.RemoveAll<ICacheProvider>();
            s.AddSingleton<ICacheProvider>(counter);
        },
    };

    /// <summary>角色变更牵动多少用户,都只发一次批量删,不发单键删。</summary>
    [Fact]
    public async Task 角色变更时权限与范围各走一次批量删()
    {
        var counter = new CountingCacheProvider();
        using var f = Factory(counter);
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<IRepository<SysRole>>();
        var userRoles = sp.GetRequiredService<IRepository<SysUserRole>>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();

        var role = new SysRole { Name = "批量失效", Code = "batch-invalidate", Enabled = true };
        await roles.InsertAsync(role);
        for (var i = 0; i < 5; i++)
        {
            var u = new SysUser { Account = $"batch-inv-{i}", Name = "批量", Password = "x", Enabled = true };
            await users.InsertAsync(u);
            await userRoles.InsertAsync(new SysUserRole { UserId = u.Id, RoleId = role.Id });
        }

        counter.Reset();
        await sp.GetRequiredService<IRbacService>().InvalidateByRoleAsync(role.Id);

        Assert.Equal(2, counter.RemoveManyCalls);          // 权限一次、范围一次
        Assert.Equal(0, counter.Removes("perm:"));         // 一个单键删都不该有
        Assert.Equal(0, counter.Removes("scope:"));
    }

    /// <summary>进程内实现没覆写批量删,默认接口实现要真的把每个键都删掉。</summary>
    [Fact]
    public async Task 默认批量删逐键生效()
    {
        var counter = new CountingCacheProvider();
        var keys = new[] { "perm:9001", "perm:9002", "perm:9003" };
        foreach (var k in keys) await counter.SetAsync(k, 1L);

        await counter.RemoveManyAsync(keys);

        foreach (var k in keys) Assert.Equal(0L, await counter.GetAsync<long>(k));
    }

    [Fact]
    public async Task 空名单不炸()
    {
        var counter = new CountingCacheProvider();
        await counter.RemoveManyAsync([]);
        Assert.Equal(1, counter.RemoveManyCalls);
    }
}
