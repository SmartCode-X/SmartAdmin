using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartAdmin.AspNetCore;
using SmartAdmin.Caching.Redis;
using SmartAdmin.Core;
using SmartAdmin.Excel;
using SmartAdmin.Services;

namespace SmartAdmin.Tests;

/// <summary>
/// 装配期契约:重复调用幂等、生产缺配置节即拒、可选包顺序错即拒。
/// 三条都是"静默失败"的堵口——托管服务被起两份、库悄悄跑在 SQLite 上、导出到用的时候才抛 46001。
/// </summary>
public class StartupContractTests
{
    private static WebApplicationBuilder Builder(string environment, Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        // CreateBuilder 已读过物理 appsettings.json;清空后只留本用例的内存配置,避免测试目录里的文件干扰
        builder.Configuration.Sources.Clear();
        if (settings is not null) builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static Dictionary<string, string?> Minimal() => new()
    {
        ["SmartAdmin:Database:DbType"] = "Sqlite",
        ["SmartAdmin:Database:ConnectionString"] = "Data Source=:memory:",
        ["SmartAdmin:Jwt:SecretKey"] = "smart-startup-contract-test-signing-key-32plus",
        ["SmartAdmin:Id:WorkerId"] = "3",
    };

    /// <summary>
    /// 已拆除的 Level3 总档:配了就启动即拒,不静默忽略。忽略它等于把 TOTP 与 Cookie 会话一起悄悄关掉。
    /// </summary>
    [Theory]
    [InlineData("SmartAdmin:Security:Profile", "Level3")]
    [InlineData("SmartAdmin:Security:Level3:CookieDomain", ".example.com")]
    public void Retired_level3_keys_are_rejected_at_startup(string key, string value)
    {
        var settings = Minimal();
        settings[key] = value;
        var builder = Builder(Environments.Development, settings);

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddSmartAdmin(builder.Configuration));

        Assert.Contains("Totp:Enabled", ex.Message);            // 错误里带迁移去处
        Assert.Contains("Session:CookieMode", ex.Message);
    }

    /// <summary>没配这两个键时不能误报(退役检测只认显式配置,不认默认值)。</summary>
    [Fact]
    public void Clean_configuration_passes_the_retired_key_check()
    {
        var builder = Builder(Environments.Development, Minimal());
        builder.Services.AddSmartAdmin(builder.Configuration);   // 不抛即可
        Assert.Single(builder.Services, d => d.ServiceType == typeof(SmartAdminRegistered));
    }

    /// <summary>装配两次不能把托管服务注册成两份(调度器、文件回收、限流刷新各起两个 = 双发任务、双份清扫)。</summary>
    [Fact]
    public void AddSmartAdmin_twice_registers_hosted_services_once()
    {
        var builder = Builder(Environments.Development, Minimal());
        builder.Services.AddSmartAdmin(builder.Configuration);
        var afterFirst = builder.Services.Count(d => d.ServiceType == typeof(IHostedService));

        builder.Services.AddSmartAdmin(builder.Configuration);

        Assert.Equal(afterFirst, builder.Services.Count(d => d.ServiceType == typeof(IHostedService)));
        Assert.Single(builder.Services, d => d.ServiceType == typeof(SmartAdminRegistered));
    }

    [Fact]
    public void AddSmartAdminWorker_twice_registers_hosted_services_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Minimal()).Build();

        services.AddSmartAdminWorker(config);
        var afterFirst = services.Count(d => d.ServiceType == typeof(IHostedService));
        services.AddSmartAdminWorker(config);

        Assert.Equal(afterFirst, services.Count(d => d.ServiceType == typeof(IHostedService)));
    }

    /// <summary>
    /// 生产 + 读不到 SmartAdmin 节 = 数据库、超管密码、机器号全落默认值,而这三项都不会报错。
    /// 消费者改了节名或环境变量前缀时最容易这样,故启动即拒。
    /// </summary>
    [Fact]
    public void Production_without_smartadmin_section_fails_fast()
    {
        var builder = Builder(Environments.Production, new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" });

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Services.AddSmartAdmin(builder.Configuration));
        Assert.Contains("SmartAdmin", ex.Message);
    }

    /// <summary>代码侧 configure 覆写视同已配置:不读文件的宿主(容器里全用代码配)不该被这道闸挡住。</summary>
    [Fact]
    public void Production_without_section_but_with_code_configure_is_allowed()
    {
        var builder = Builder(Environments.Production);
        var ex = Record.Exception(() => builder.Services.AddSmartAdmin(builder.Configuration, o =>
        {
            o.Database.ConnectionString = "Data Source=:memory:";
            o.Jwt.SecretKey = "smart-startup-contract-test-signing-key-32plus";
            o.Id.WorkerId = 3;
        }));
        Assert.Null(ex);
    }

    /// <summary>零配置可跑是产品承诺:非生产环境缺节照常装配(默认 SQLite + 随机超管密码)。</summary>
    [Fact]
    public void Development_without_section_still_boots()
    {
        var builder = Builder(Environments.Development);
        var ex = Record.Exception(() => builder.Services.AddSmartAdmin(builder.Configuration));
        Assert.Null(ex);
    }

    /// <summary>可选包晚于内核调用 = TryAdd 全部落空;没有这道检查的话,要等到第一次导出才会抛 46001,现在装配期就直接报。</summary>
    [Fact]
    public void AddSmartAdminExcel_after_kernel_throws()
    {
        var builder = Builder(Environments.Development, Minimal());
        builder.Services.AddSmartAdmin(builder.Configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Services.AddSmartAdminExcel());
        Assert.Contains("AddSmartAdmin()", ex.Message);
    }

    /// <summary>同上:Redis 晚注册会静默退回进程内缓存,多副本下强退与限流计数全部失真。</summary>
    [Fact]
    public void AddSmartAdminRedisCache_after_kernel_throws()
    {
        var builder = Builder(Environments.Development, Minimal());
        builder.Services.AddSmartAdmin(builder.Configuration);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Services.AddSmartAdminRedisCache("localhost:6379"));
        Assert.Contains("AddSmartAdmin", ex.Message);
    }
}
