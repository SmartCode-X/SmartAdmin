using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SmartAdmin.Testing;

/// <summary>
/// 集成测试宿主工厂:用你的应用(<typeparamref name="TEntryPoint"/> 通常是 <c>Program</c>)做被测宿主,
/// 每个实例一个独立的库(默认 SQLite 文件;按 <see cref="TestDb"/> 的环境变量切到 MySQL / SqlServer / PostgreSQL),
/// 固定超管密码与 JWT 密钥,环境钉在 <c>Development</c>(<c>WebApplicationFactory</c> 不读 launchSettings,
/// 落到 Production 时 CodeFirst 自动建表默认关闭,种子会因为表不存在而启动失败)。
/// <para>最省的用法:<c>public sealed class AppFactory : AdminAppFactory&lt;Program&gt; { }</c>,
/// 然后 <c>IClassFixture&lt;AppFactory&gt;</c> 或每个用例 <c>using var f = new AppFactory();</c>。
/// 要改默认(禁用模块、额外配置、替换服务)用对象初始化器,要改工厂本身覆写 <see cref="ConfigureWebHost"/>。</para>
/// </summary>
public class AdminAppFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
{
    /// <summary>默认超管密码(<c>SmartAdmin:Seed:AdminPassword</c>)。</summary>
    public const string DefaultAdminPassword = "Test@123456";

    /// <summary>本实例的 SQLite 库文件(默认唯一;幂等测试可传同一路径复用);其它方言按它派生独立库。</summary>
    public string DbPath { get; init; } = Path.Combine(Path.GetTempPath(), $"smart-it-{Guid.NewGuid():N}.db");

    /// <summary>禁用的内置模块(<c>SmartAdmin:Api:DisabledModules</c>);默认不禁。</summary>
    public IReadOnlyList<string> DisabledModules { get; init; } = [];

    /// <summary>Dispose 时是否删库(幂等测试需跨实例复用库,置 false 自行清理)。</summary>
    public bool DeleteDbOnDispose { get; init; } = true;

    /// <summary>每测试的服务覆盖(ConfigureTestServices;用于 Replace 框架服务)。</summary>
    public Action<IServiceCollection>? Overrides { get; init; }

    /// <summary>额外的配置项覆盖(在 AddSmartAdmin 绑定前生效,如 CORS 源、会话模式等)。</summary>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    /// <summary>宿主环境名(默认 Development;生产建表闸门用例传 "Production")。</summary>
    public string EnvironmentName { get; init; } = "Development";

    /// <summary>超管密码。</summary>
    public string AdminPassword { get; init; } = DefaultAdminPassword;

    /// <summary>固定 ≥32 字节 JWT 密钥:避免各测试并发写同一 ./data/dev-jwt.key 文件。</summary>
    public string JwtSecretKey { get; init; } = "smart-integration-test-signing-key-please-keep-32plus";

    /// <summary>
    /// 要一个真正的空库,让内核从零建表播种。默认 false = 从模板库克隆(见 <see cref="TestDb.CloneFromTemplate"/>),
    /// 省掉每个宿主的建表与播种。非 Development 环境、改了 <c>SmartAdmin:Seed:*</c> 或 <c>SmartAdmin:Database:*</c>
    /// 的用例自动按空库处理,不用手写:模板是按默认配置建的,种子内容、建表开关都跟它不同。
    /// <para>显式置 true 的场合:用例的前提就是"首启从零建表播种"(升级路径、缺表报错、种子首插计数)。
    /// 坑:模板里超管密码是默认密码的哈希,替换了 IPasswordHasher 又要登录的用例也得置 true。</para>
    /// </summary>
    public bool FreshDatabase { get; init; }

    private bool UseTemplate => !FreshDatabase
        && EnvironmentName == "Development"
        && AdminPassword == DefaultAdminPassword
        && (Settings is null || !Settings.Keys.Any(k =>
            k.StartsWith("SmartAdmin:Seed:", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("SmartAdmin:Database:", StringComparison.OrdinalIgnoreCase)));

    /// <summary>配置被测宿主:接好 <see cref="TestDb"/> 选库、固定超管密码与 JWT 密钥、按属性覆盖设置与服务。</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        // DB 选择走 TestDb:默认 SQLite(DbPath 文件);其它方言按 DbPath 派生独立库(同 DbPath 共享库,幂等用例用)。
        // 默认形态从模板克隆(建好表、播好种子),要空库的走 ConnectionString。
        builder.UseSetting("SmartAdmin:Database:DbType", TestDb.DbType);
        builder.UseSetting("SmartAdmin:Database:ConnectionString",
            UseTemplate ? TestDb.CloneFromTemplate(DbPath, DbPath, StartTemplateHost) : TestDb.ConnectionString(DbPath, DbPath));
        builder.UseSetting("SmartAdmin:Seed:AdminPassword", AdminPassword);
        builder.UseSetting("SmartAdmin:Jwt:SecretKey", JwtSecretKey);
        builder.UseSetting("SmartAdmin:Security:RateLimit:Enabled", "false");   // 默认关限流,隔离既有测试;限流用例经 Settings 显式开
        // 操作日志默认同步落库:绝大多数用例是"发个请求,然后立刻断言日志表里有那一行",异步写手会让它们变成竞态。
        builder.UseSetting("SmartAdmin:Logging:OpLog:Sync", "true");
        // 机器号锁目录:测试进程私有,不碰机器级的 ProgramData;槽位随宿主 Dispose 释放,并发存活的宿主各拿各的号
        builder.UseSetting("SmartAdmin:Id:WorkerIdLockDir", Path.Combine(Path.GetTempPath(), "smart-it-workerid"));
        for (var i = 0; i < DisabledModules.Count; i++)
            builder.UseSetting($"SmartAdmin:Api:DisabledModules:{i}", DisabledModules[i]);
        if (Settings != null)
            foreach (var kv in Settings) builder.UseSetting(kv.Key, kv.Value);

        if (Overrides != null) builder.ConfigureTestServices(Overrides);
    }

    /// <summary>
    /// 建模板库时起的那个宿主:默认形态、空库、不删库。子类改了默认(比如禁用模块)想让模板也照样,覆写它返回自己的实例。
    /// 返回值被释放即模板建好。
    /// </summary>
    protected virtual IDisposable StartTemplateHost(string identity)
    {
        var host = new AdminAppFactory<TEntryPoint> { DbPath = identity, FreshDatabase = true, DeleteDbOnDispose = false };
        _ = host.CreateClient();   // 宿主起来 = CodeFirst 与全部种子跑完
        return host;
    }

    /// <summary>先释放宿主(关闭 SqlSugar 连接),再按 <see cref="DeleteDbOnDispose"/> 清理测试库。</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);   // 先释放宿主(关闭 SqlSugar 连接),再清理库
        if (disposing && DeleteDbOnDispose)
            TestDb.Cleanup(DbPath, DbPath);   // SQLite 删文件 / 其它方言删库
    }
}
