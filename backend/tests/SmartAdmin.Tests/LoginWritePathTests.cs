using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 登录成功那一下往库里写了什么。
/// <para>若登录写回是整行回写:管理员在"读出用户"到"登录写回"这中间改了角色、机构、启用状态,
/// 会被这次登录悄悄抹掉——一个安静的丢失更新,而且只在有人正好同时操作时才出现。</para>
/// </summary>
public class LoginWritePathTests
{
    [Fact]
    public async Task 登录只更新最近登录时间不覆盖并发改动()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var before = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotNull(before);

        // 模拟"另一个人在登录处理期间改了这一行":登录读到的是旧值,写回时不该把这次改动盖掉
        await users.Db.Updateable<SysUser>()
            .SetColumns(u => u.Nickname == "并发改的昵称")
            .Where(u => u.Id == before!.Id)
            .ExecuteCommandAsync();

        await auth.LoginAsync(new LoginInput { Account = "superAdmin", Password = "Test@123456" });

        var after = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.Equal("并发改的昵称", after!.Nickname);      // 没被登录写回抹掉
        Assert.NotNull(after.LastSuccessfulLoginAt);        // 该更新的那一列确实更新了
    }

    /// <summary>迭代次数改了之后,用户下次登录时哈希被无感重算——不用他改密。</summary>
    [Fact]
    public async Task 迭代次数变化时登录无感重哈希()
    {
        using var f = new AdminAppFactory
        {
            Settings = new Dictionary<string, string?>
            {
                // 与内核默认的 60 万不同,于是种子里那份哈希"过期"了
                ["SmartAdmin:Security:Password:Pbkdf2Iterations"] = "150000",
            },
            FreshDatabase = true,   // 模板库里的哈希是按默认迭代数种的,这条要自己从零播种
        };
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // 先塞一份按旧参数(60 万)算出来的哈希
        var user = await users.GetFirstAsync(u => u.Account == "superAdmin");
        var legacy = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 600_000 } }).Hash("Test@123456");
        await users.Db.Updateable<SysUser>()
            .SetColumns(u => u.Password == legacy)
            .Where(u => u.Id == user!.Id)
            .ExecuteCommandAsync();
        Assert.True(hasher.NeedsRehash(legacy));

        await auth.LoginAsync(new LoginInput { Account = "superAdmin", Password = "Test@123456" });

        var after = await users.GetFirstAsync(u => u.Account == "superAdmin");
        Assert.NotEqual(legacy, after!.Password);           // 换了新串
        Assert.False(hasher.NeedsRehash(after.Password));   // 已经是当前参数
        Assert.True(hasher.Verify("Test@123456", after.Password));   // 而且照样能登
    }

    /// <summary>站点信息是匿名热路径:第二次读必须走缓存,不再逐个键往返。</summary>
    [Fact]
    public async Task 站点信息整体缓存()
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
        var c = f.CreateClient();

        await c.GetAsync("/api/v1/sys/config/site");   // 第一次:装配并写缓存
        counter.Reset();
        await c.GetAsync("/api/v1/sys/config/site");   // 第二次:整体命中

        Assert.Equal(1, counter.Gets("config:site"));
        Assert.Equal(0, counter.Gets("config:sys."));   // 不再逐个配置键读
    }

    /// <summary>改了配置就得让登录页立刻看到新的:站点信息是合成值,一律跟着清。</summary>
    [Fact]
    public async Task 配置变更后站点信息缓存失效()
    {
        using var f = new AdminAppFactory();
        var admin = f.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await admin.LoginToken("superAdmin", "Test@123456"));

        using var scope = f.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheProvider>();

        await config.GetSiteInfoAsync();
        Assert.NotNull(await cache.GetAsync<SiteInfoOutput>(CacheKeys.SiteInfo));

        await config.SaveValuesAsync([new ConfigBatchItem { ConfigKey = "sys.site.title", ConfigValue = "新标题" }]);

        Assert.Null(await cache.GetAsync<SiteInfoOutput>(CacheKeys.SiteInfo));
        Assert.Equal("新标题", (await config.GetSiteInfoAsync()).Title);
    }
}

/// <summary>口令哈希参数的配置化与重算判定。</summary>
public class Pbkdf2IterationsTests
{
    [Fact]
    public void 产出的哈希串记着当次用的迭代数()
    {
        var hasher = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 120_000 } });

        var hash = hasher.Hash("pw");

        Assert.StartsWith("pbkdf2-sha256.120000.", hash, StringComparison.Ordinal);
        Assert.True(hasher.Verify("pw", hash));
    }

    /// <summary>一次手滑把它调到几百,等于把口令哈希削成明文级,所以有下限。</summary>
    [Fact]
    public void 低于下限按下限算()
    {
        var hasher = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 500 } });

        Assert.StartsWith($"pbkdf2-sha256.{AdminPasswordOptions.MIN_ITERATIONS}.", hasher.Hash("pw"), StringComparison.Ordinal);
    }

    [Fact]
    public void 参数一致时不需要重算()
    {
        var hasher = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 150_000 } });

        Assert.False(hasher.NeedsRehash(hasher.Hash("pw")));
    }

    [Fact]
    public void 参数不同时需要重算()
    {
        var old = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 150_000 } }).Hash("pw");
        var current = new Pbkdf2PasswordHasher(new AdminSecurityOptions { Password = new AdminPasswordOptions { Pbkdf2Iterations = 300_000 } });

        Assert.True(current.NeedsRehash(old));
        Assert.True(current.Verify("pw", old));   // 但旧串仍然验得过,用户不会被挡在门外
    }

    [Fact]
    public void 认不出的算法段一律重算()
    {
        var hasher = new Pbkdf2PasswordHasher();

        Assert.True(hasher.NeedsRehash("bcrypt.12.abc.def"));
        Assert.False(hasher.NeedsRehash(""));   // 空哈希该走的是设初始密码那条路,不是重算
    }
}
