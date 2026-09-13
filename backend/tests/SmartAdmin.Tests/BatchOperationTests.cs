using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 批量操作的两件事:别一条一条打库,以及失败时别留下删了一半的现场。
/// <para>「删了一半」尤其难查——调用方拿到一个异常,以为什么都没发生,实际前几条已经落库,
/// 而日志里没有任何东西说明是哪几条。</para>
/// </summary>
public class BatchOperationTests
{
    /// <summary>一个用户的多个会话下线时,两条 UPDATE 覆盖全体,不是每个会话各来一遍。</summary>
    [Fact]
    public async Task 批量吊销只发一次批量删缓存()
    {
        var counter = new CountingCacheProvider();
        using var f = new AdminAppFactory
        {
            Overrides = s =>
            {
                s.RemoveAll<ICacheProvider>();
                s.AddSingleton<ICacheProvider>(counter);
            },
        };
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var sessions = sp.GetRequiredService<ISessionService>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var tokens = sp.GetRequiredService<ITokenProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(user);
        for (var i = 0; i < 4; i++)
        {
            var sid = Guid.CreateVersion7().ToString("N");
            var pair = tokens.Create(
                new TokenSubject(user!.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
                TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
            await sessions.OpenAsync(user, sid, pair);
        }

        counter.Reset();
        await sessions.RevokeAllForUserAsync(user!.Id);

        Assert.Equal(1, counter.RemoveManyCalls);
        Assert.Equal(0, counter.Removes("session:"));
    }

    /// <summary>批量吊销之后,那些会话必须全都不活跃了(省往返不能省掉正确性)。</summary>
    [Fact]
    public async Task 批量吊销后会话全部失活()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var sessions = sp.GetRequiredService<ISessionService>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var tokens = sp.GetRequiredService<ITokenProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        var sids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var sid = Guid.CreateVersion7().ToString("N");
            var pair = tokens.Create(
                new TokenSubject(user!.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
                TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
            await sessions.OpenAsync(user, sid, pair);
            sids.Add(sid);
        }
        foreach (var sid in sids) Assert.True(await sessions.IsActiveAsync(sid));

        await sessions.RevokeAllForUserAsync(user!.Id);

        foreach (var sid in sids) Assert.False(await sessions.IsActiveAsync(sid));
    }

    /// <summary>保留当前会话的批量下线:只有它还活着。</summary>
    [Fact]
    public async Task 批量吊销可保留指定会话()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var sessions = sp.GetRequiredService<ISessionService>();
        var users = sp.GetRequiredService<IRepository<SysUser>>();
        var tokens = sp.GetRequiredService<ITokenProvider>();

        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        var sids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var sid = Guid.CreateVersion7().ToString("N");
            var pair = tokens.Create(
                new TokenSubject(user!.Id, user.Account, sid, user.IsSuperAdmin, user.OrgId),
                TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));
            await sessions.OpenAsync(user, sid, pair);
            sids.Add(sid);
        }

        await sessions.RevokeAllForUserExceptAsync(user!.Id, sids[1]);

        Assert.False(await sessions.IsActiveAsync(sids[0]));
        Assert.True(await sessions.IsActiveAsync(sids[1]));
        Assert.False(await sessions.IsActiveAsync(sids[2]));
    }

    /// <summary>字典项批删撞上种子保护时整批回滚,不能删掉一半。</summary>
    [Fact]
    public async Task 字典项批删失败时整批回滚()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var dict = sp.GetRequiredService<IDictService>();
        var items = sp.GetRequiredService<IRepository<SysDictItem>>();

        var typeId = await dict.AddTypeAsync(new DictTypeInput
        {
            Code = "batch-rollback", Name = "批量回滚", Sort = 1, Enabled = true,
        });
        Assert.True(typeId > SmartSeedIds.KernelMax);

        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
            ids.Add(await dict.AddItemAsync(new DictItemInput
            {
                DictTypeCode = "batch-rollback", Label = $"项{i}", Value = $"v{i}", Sort = i, Enabled = true,
            }));

        // 混进一个内核种子项 Id:守卫会在删之前抛,自建的那三条一条都不该少
        var seedItemId = await items.AsQueryable().ClearFilter<ISoftDelete>()
            .Where(i => i.Id <= SmartSeedIds.KernelMax).Select(i => i.Id).FirstAsync();
        Assert.True(seedItemId > 0);

        var ex = await Assert.ThrowsAsync<AdminException>(
            () => dict.DeleteItemsBatchAsync([.. ids, seedItemId]));
        Assert.Equal(ErrorCode.SeedDataProtected, ex.Code);

        foreach (var id in ids)
            Assert.NotNull(await items.GetByIdAsync(id));
    }
}
