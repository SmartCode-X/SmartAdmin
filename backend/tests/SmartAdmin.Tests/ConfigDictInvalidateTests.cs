using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 配置 / 字典的读穿透缓存只在服务自己增删改时失效。行被绕过服务改掉(直接改库、迁移脚本、共库的别的系统)之后,
/// 调用方用 <c>InvalidateAsync</c> 让缓存立刻收敛,变更事件照发;管理员的「清配置缓存」连站点信息一起清。
/// </summary>
public class ConfigDictInvalidateTests
{
    private const string KEY = "sys.job.logRetentionDays";

    private static Task<int> UpdateConfigInDbAsync(IServiceProvider sp, string key, string value) =>
        sp.GetRequiredService<ISqlSugarClient>().Updateable<SysConfig>()
            .SetColumns(c => c.ConfigValue == value)
            .Where(c => c.ConfigKey == key)
            .ExecuteCommandAsync();

    /// <summary>订阅一类事件,拿到第一条满足条件的就完成;事件总线是后台派发,须等。</summary>
    private static (Task<T> Received, IDisposable Subscription) Await<T>(IServiceProvider sp, Func<T, bool> match) where T : notnull
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub = sp.GetRequiredService<IEventBus>().Subscribe<T>((e, _) =>
        {
            if (match(e)) tcs.TrySetResult(e);
            return Task.CompletedTask;
        });
        return (tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)), sub);
    }

    [Fact]
    public async Task Config_invalidate_converges_a_value_changed_outside_the_service_and_publishes_the_change()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfigService>();

        Assert.Equal("30", await config.GetValueByKeyAsync(KEY));   // 读一次,进缓存
        Assert.Equal(1, await UpdateConfigInDbAsync(sp, KEY, "7"));
        Assert.Equal("30", await config.GetValueByKeyAsync(KEY));   // 服务不知道库变了

        var (received, sub) = Await<ConfigChangedEvent>(sp, e => e.Key == KEY);
        using (sub)
        {
            await config.InvalidateAsync(KEY);
            Assert.Equal("7", await config.GetValueByKeyAsync(KEY));
            await received;
        }
    }

    [Fact]
    public async Task Config_invalidate_also_drops_the_site_info_built_from_that_key()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfigService>();

        Assert.Equal("SmartAdmin", (await config.GetSiteInfoAsync()).Title);
        await UpdateConfigInDbAsync(sp, "sys.site.title", "直接改库的标题");

        await config.InvalidateAsync("sys.site.title");
        Assert.Equal("直接改库的标题", (await config.GetSiteInfoAsync()).Title);
    }

    [Fact]
    public async Task Dict_invalidate_converges_items_changed_outside_the_service_and_publishes_the_change()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var dict = sp.GetRequiredService<IDictService>();

        var code = "inv_" + Guid.CreateVersion7().ToString("N")[..8];
        await dict.AddTypeAsync(new DictTypeInput { Code = code, Name = "失效测试", Enabled = true });
        var itemId = await dict.AddItemAsync(new DictItemInput { DictTypeCode = code, Label = "旧文本", Value = "1", Enabled = true });
        Assert.Equal("旧文本", Assert.Single(await dict.GetItemsByTypeAsync(code)).Label);   // 进缓存

        await sp.GetRequiredService<ISqlSugarClient>().Updateable<SysDictItem>()
            .SetColumns(i => i.Label == "新文本")
            .Where(i => i.Id == itemId)
            .ExecuteCommandAsync();
        Assert.Equal("旧文本", Assert.Single(await dict.GetItemsByTypeAsync(code)).Label);

        var (received, sub) = Await<DictChangedEvent>(sp, e => e.TypeCode == code);
        using (sub)
        {
            await dict.InvalidateAsync(code);
            Assert.Equal("新文本", Assert.Single(await dict.GetItemsByTypeAsync(code)).Label);
            await received;
        }
    }

    [Fact]
    public async Task Flush_config_also_drops_the_cached_site_info()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfigService>();

        Assert.Equal("SmartAdmin", (await config.GetSiteInfoAsync()).Title);   // 站点信息整体进缓存
        await UpdateConfigInDbAsync(sp, "sys.site.title", "直接改库的标题");

        await sp.GetRequiredService<ICacheAdminService>().FlushConfigAsync();
        Assert.Equal("直接改库的标题", (await config.GetSiteInfoAsync()).Title);
    }
}
