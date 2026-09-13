using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SmartAdmin.TestHost;

namespace SmartAdmin.Tests;

/// <summary>
/// 种子契约的三件新事:明细表(<see cref="PrimaryId"/>)可种子化、库就绪钩子、升级同步的列白名单。
/// <para>三件都是没有这些能力时消费者只能绕开的地方:给明细表凭空加审计列、自建托管服务并小心排在
/// <c>AddSmartAdmin</c> 之后、以及"这张表整体不敢开同步"。</para>
/// </summary>
public class SeedContractTests
{
    [Fact]
    public async Task PrimaryId_entity_can_be_seeded()
    {
        using var f = new AdminAppFactory { FreshDatabase = true };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        var rows = await db.Queryable<SampleWidgetDetail>().OrderBy(x => x.Id).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(SampleWidgetDetailSeed.SeedIdA, rows[0].Id);
        Assert.Equal("detail-a", rows[0].Name);
    }

    /// <summary>同一个库再启一次,行数不变(幂等按主键判存,与 AuditEntity 种子同一条路径)。</summary>
    [Fact]
    public async Task PrimaryId_seed_is_idempotent_across_restarts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"smart-pid-seed-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true })
                _ = first.CreateClient();

            using var second = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true };
            using var scope = second.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

            Assert.Equal(2, await db.Queryable<SampleWidgetDetail>().CountAsync());
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }

    /// <summary>钩子必须晚于种子:调用时 widget 种子的 2 行已在库里。</summary>
    [Fact]
    public void Ready_hook_runs_after_seeds()
    {
        using var f = new AdminAppFactory { FreshDatabase = true };
        _ = f.CreateClient();   // 触发宿主启动
        var record = f.Services.GetRequiredService<ReadyHookRecord>();

        Assert.NotNull(record.Context);
        Assert.True(record.Context!.SeedRan);
        Assert.True(record.Context.CodeFirstRan);
        Assert.Equal(SysSchemaVersion.Current, record.Context.CurrentSchemaVersion);
        Assert.Equal(2, record.WidgetCount);   // 种子已播完
    }

    /// <summary>关掉种子也要调钩子,并在上下文里如实说明种子没跑。</summary>
    [Fact]
    public void Ready_hook_runs_even_when_seeding_is_disabled()
    {
        using var f = new AdminAppFactory
        {
            FreshDatabase = true,
            Settings = new Dictionary<string, string?> { ["SmartAdmin:Database:EnableSeed"] = "false" },
        };
        _ = f.CreateClient();
        var record = f.Services.GetRequiredService<ReadyHookRecord>();

        Assert.NotNull(record.Context);
        Assert.False(record.Context!.SeedRan);
        Assert.Equal(0, record.WidgetCount);
    }

    /// <summary>
    /// 配置项升级同步只碰白名单里的列:内核改过的展示名刷新,用户改过的值原样保留。
    /// <para>手法同 <c>SeedUpgradeTests</c>:把库里的种子版本行改成旧值,下次启动即视为一次升级。</para>
    /// </summary>
    [Fact]
    public async Task Config_seed_upgrade_refreshes_name_but_keeps_user_value()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"smart-cfg-sync-{Guid.NewGuid():N}.db");
        try
        {
            long configId;
            using (var first = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true })
            {
                _ = first.CreateClient();
                using var scope = first.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

                var row = await db.Queryable<SysConfig>().FirstAsync(x => x.Id == 1);
                configId = row.Id;

                // 用户在配置中心改了值,又顺手改了展示名;并把版本行退回旧版模拟一次内核升级
                await db.Updateable<SysConfig>()
                    .SetColumns(x => new SysConfig { ConfigValue = "用户改过的站点标题", Name = "用户改过的展示名" })
                    .Where(x => x.Id == configId)
                    .ExecuteCommandAsync();
                await db.Updateable<SysSchemaVersion>()
                    .SetColumns(x => new SysSchemaVersion { Version = "0.0.1" })
                    .Where(x => x.Id == 1)
                    .ExecuteCommandAsync();
            }

            using var second = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true };
            using var scope2 = second.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var after = await db2.Queryable<SysConfig>().FirstAsync(x => x.Id == configId);

            Assert.Equal("站点标题", after.Name);                       // 元信息刷回种子值
            Assert.Equal("用户改过的站点标题", after.ConfigValue);        // 用户的值原样保留
        }
        finally
        {
            TestDb.Cleanup(dbPath, dbPath);
        }
    }
}
