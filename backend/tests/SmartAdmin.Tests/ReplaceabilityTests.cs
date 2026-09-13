using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SmartAdmin.TestHost;

namespace SmartAdmin.Tests;

/// <summary>
/// 可替换机制回归锁(用例名是产品承诺,不随手重命名)。证明:框架服务可替换、鉴权步骤可覆写、
/// 模块可禁用、用户控制器/种子/实体即插即用。
/// </summary>
public class ReplaceabilityTests
{
    [Fact]
    public void ReplaceService_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IPasswordHasher, FakeHasher>()),
        };
        Assert.IsType<FakeHasher>(f.Services.GetRequiredService<IPasswordHasher>());
    }

    [Fact]
    public void ReplaceSmsSender_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<ISmsSender, FakeSmsSender>()),
        };
        Assert.IsType<FakeSmsSender>(f.Services.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void ReplaceEmailSender_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IEmailSender, FakeEmailSender>()),
        };
        Assert.IsType<FakeEmailSender>(f.Services.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void ReplaceRealtimePublisher_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IRealtimePublisher, FakeRealtimePublisher>()),
        };
        Assert.IsType<FakeRealtimePublisher>(f.Services.GetRequiredService<IRealtimePublisher>());
    }

    // ── 定时任务的可替换面 ─────────────────────────

    /// <summary>
    /// 定时任务相关可替换接口的「<b>前置</b>注册即胜出」——这才是 TryAdd 契约的真判据。
    /// <para>注意别照抄本文件其它用例的 <c>Overrides = s => s.Replace(...)</c> 写法来测 TryAdd:那是
    /// <c>ConfigureTestServices</c>,跑在 <c>AddSmartAdmin</c> <b>之后</b>,TryAdd 改成 Add 它照样绿——
    /// 它证明的是"可替换",不是"TryAdd 注册"。故此处直接在裸容器里前置注册再调 <c>AddSmartAdminServices()</c>。</para>
    /// <para>变异:把 ServicesSetup 里这三行任一的 TryAdd 改成 Add → 内置实现后注册即胜出 → 本条红。</para>
    /// </summary>
    [Fact]
    public async Task PreRegisteredJobServices_ShouldWinOverBuiltIns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(new AdminIdOptions());
        services.AddSingleton(new AdminJobsOptions());
        // 消费者的前置注册(在 AddSmartAdmin* 之前)
        services.AddScoped<IJobService, FakeJobService>();
        services.AddSingleton<IJobHandlerResolver, FakeJobHandlerResolver>();
        services.AddSingleton<JobSchedulerService, SubclassedScheduler>();

        var dbOptions = new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "DataSource=:memory:" };
        services.AddSingleton(dbOptions);
        services.AddSmartAdminSqlSugar(dbOptions, [typeof(ServicesSetup).Assembly]);
        services.AddSmartAdminServices();

        // 容器必须异步释放:ChannelEventBus 只实现 IAsyncDisposable,同步 Dispose 会直接抛
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeJobService>(scope.ServiceProvider.GetRequiredService<IJobService>());
        Assert.IsType<FakeJobHandlerResolver>(sp.GetRequiredService<IJobHandlerResolver>());
        Assert.IsType<SubclassedScheduler>(sp.GetRequiredService<JobSchedulerService>());
    }

    /// <summary>
    /// core 服务(IPasswordHasher / IAuthService / IFileService)的前置注册 TryAdd 契约。
    /// 变异:把 ServicesSetup 里任一的 TryAdd 改成 Add → 内置实现后注册即胜出 → 本条红。
    /// </summary>
    [Fact]
    public async Task PreRegisteredCoreServices_ShouldWinOverBuiltIns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AdminCacheOptions());
        services.AddSingleton(new AdminIdOptions());
        services.AddSingleton(new AdminJobsOptions());
        services.AddSingleton<IPasswordHasher, FakeHasher>();
        services.AddScoped<IAuthService, FakeAuthService>();
        services.AddScoped<IFileService, FakeFileService>();

        var dbOptions = new AdminDatabaseOptions { DbType = "Sqlite", ConnectionString = "DataSource=:memory:" };
        services.AddSingleton(dbOptions);
        services.AddSmartAdminSqlSugar(dbOptions, [typeof(ServicesSetup).Assembly]);
        services.AddSmartAdminServices();

        await using var sp = services.BuildServiceProvider();
        Assert.IsType<FakeHasher>(sp.GetRequiredService<IPasswordHasher>());
        await using var scope = sp.CreateAsyncScope();
        Assert.IsType<FakeAuthService>(scope.ServiceProvider.GetRequiredService<IAuthService>());
        Assert.IsType<FakeFileService>(scope.ServiceProvider.GetRequiredService<IFileService>());
    }

    /// <summary>
    /// 选项 POCO 的「前置注册即胜出」。若这一整类走 <c>AddSingleton</c>,内核后注册反而会赢,
    /// 消费者想换掉某个选项对象(或换成自己的派生类型)就只能靠 configure lambda。
    /// <para>变异:把 <c>SmartAdminOptionsSetup</c> 里任一 TryAddSingleton 改回 AddSingleton → 对应那条红。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(OptionTypes))]
    public void PreRegisteredOptions_ShouldWinOverBuiltIns(Type optionType)
    {
        var services = new ServiceCollection();
        var mine = Activator.CreateInstance(optionType)!;
        services.AddSingleton(optionType, mine);   // 消费者的前置注册

        services.AddSmartAdminOptions(new SmartAdminOptions());

        using var sp = services.BuildServiceProvider();
        Assert.Same(mine, sp.GetRequiredService(optionType));
    }

    public static TheoryData<Type> OptionTypes() =>
    [
        typeof(SmartAdminOptions), typeof(AdminDatabaseOptions), typeof(AdminCacheOptions), typeof(AdminSeedOptions),
        typeof(AdminJwtOptions), typeof(AdminSecurityOptions), typeof(AdminUploadOptions), typeof(AdminApiOptions),
        typeof(AdminEmailOptions), typeof(AdminExternalAuthOptions), typeof(AdminRealtimeOptions),
        typeof(AdminExcelOptions), typeof(AdminJobsOptions), typeof(AdminLoggingOptions),
    ];

    /// <summary>
    /// 扩展点清单的「前置注册即胜出」逐项探针——这才是 TryAdd 契约的真判据。
    /// <para>本文件另几条 <c>Overrides = s => s.Replace(...)</c> 用例走的是 <c>ConfigureTestServices</c>,
    /// 跑在 <c>AddSmartAdmin</c> <b>之后</b>,把 TryAdd 改成 Add 它们照样绿——那证明的是"可替换",不是"TryAdd 注册"。
    /// 这里在裸容器里前置注册假实现,再跑完整装配链。</para>
    /// <para>变异:把任一扩展点的 TryAdd 改成 Add → 对应那条红。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplaceabilityContract.ExtensionPoints), MemberType = typeof(ReplaceabilityContract))]
    public async Task PreRegisteredExtensionPoint_ShouldWinOverBuiltIn(Type service, ServiceLifetime lifetime)
    {
        var services = BareContainer();
        var mine = ReplaceabilityContract.NoopProxy.Create(service);
        services.Add(new ServiceDescriptor(service, _ => mine, lifetime));   // 消费者的前置注册

        RegisterKernel(services);

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        Assert.Same(mine, scope.ServiceProvider.GetRequiredService(service));
    }

    /// <summary>
    /// 反向自清:内核注册的每个 SmartAdmin 接口都必须登记进扩展点清单(多实现集合除外)。
    /// 新增内置服务却忘了登记 → 本条红,提醒补上契约保护。
    /// </summary>
    [Fact]
    public void EveryRegisteredKernelInterface_ShouldBeDeclaredAsExtensionPoint()
    {
        var services = BareContainer();
        RegisterKernel(services);

        var declared = ReplaceabilityContract.Points.Select(p => p.Service)
            .Concat(ReplaceabilityContract.MultiImplementation)
            .Concat(ReplaceabilityContract.OpenGeneric)
            .ToHashSet();
        var missing = services
            .Select(d => d.ServiceType)
            .Where(t => t.IsInterface && t.Namespace?.StartsWith("SmartAdmin", StringComparison.Ordinal) == true)
            .Where(t => !declared.Contains(t))
            .Distinct()
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>清单里的每一项都真被内核注册(写错类型名/删掉注册都会被这条抓到)。</summary>
    [Fact]
    public void EveryDeclaredExtensionPoint_ShouldBeRegisteredByKernel()
    {
        var services = BareContainer();
        RegisterKernel(services);

        var registered = services.Select(d => d.ServiceType).ToHashSet();
        var absent = ReplaceabilityContract.Points
            .Where(p => !registered.Contains(p.Service))
            .Select(p => p.Service.Name)
            .Order()
            .ToList();

        Assert.Empty(absent);
    }

    /// <summary>开放泛型仓储的前置注册契约(<c>IRepository&lt;&gt;</c> 造不出 DispatchProxy,单独一条)。</summary>
    [Fact]
    public void PreRegisteredRepository_ShouldWinOverBuiltIn()
    {
        var services = BareContainer();
        services.Add(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(FakeRepository<>)));

        RegisterKernel(services);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<FakeRepository<SysUser>>(scope.ServiceProvider.GetRequiredService<IRepository<SysUser>>());
    }

    /// <summary>裸容器 + 装配链所需的最小前置(日志),不含任何被测扩展点。</summary>
    private static ServiceCollection BareContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>消费者真实走的那一个入口:<c>AddSmartAdmin</c> 从组合根一路装到数据层,三层的 TryAdd 一次全覆盖。</summary>
    private static void RegisterKernel(IServiceCollection services)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartAdmin:Database:DbType"] = "Sqlite",
            ["SmartAdmin:Database:ConnectionString"] = "DataSource=:memory:",
            ["SmartAdmin:Id:WorkerId"] = "0",
        }).Build();
        services.AddSmartAdmin(config);
    }

    /// <summary>消费者自己的 IAdminJob(TestHost 的 SampleJob)必须被默认解析器认得,并出现在处理器清单里。</summary>
    [Fact]
    public async Task ConsumerJobHandler_ShouldBeResolvableAndListed()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();

        var resolver = f.Services.GetRequiredService<IJobHandlerResolver>();
        var handler = await resolver.ResolveAsync(typeof(SampleJob).FullName!, scope.ServiceProvider);
        Assert.IsType<SampleJob>(handler);

        var listed = scope.ServiceProvider.GetRequiredService<IJobService>().ListHandlers();
        Assert.Contains(typeof(SampleJob).FullName, listed);
    }

    // ── 导入导出可替换接口各一条 ─────────────────

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelReader 的 TryAdd 改成 Add(覆盖消费者) → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelReader_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelReader, FakeExcelReader>()),
        };
        Assert.IsType<FakeExcelReader>(f.Services.GetRequiredService<IExcelReader>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelWriter 的 TryAdd 改成 Add → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelWriter_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelWriter, FakeExcelWriter>()),
        };
        Assert.IsType<FakeExcelWriter>(f.Services.GetRequiredService<IExcelWriter>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IExcelTemplateBuilder 的 TryAdd 改成 Add → 解析到 MissingExcelProvider → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceExcelTemplateBuilder_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Singleton<IExcelTemplateBuilder, FakeExcelTemplateBuilder>()),
        };
        Assert.IsType<FakeExcelTemplateBuilder>(f.Services.GetRequiredService<IExcelTemplateBuilder>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IImportRunner 的 TryAdd 改成 Add → 解析到 ImportRunner → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceImportRunner_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IImportRunner, FakeImportRunner>()),
        };
        using var scope = f.Services.CreateScope();
        Assert.IsType<FakeImportRunner>(scope.ServiceProvider.GetRequiredService<IImportRunner>());
    }

    /// <summary>
    /// 变异:把 ServicesSetup 里 IDictTextResolver 的 TryAdd 改成 Add → 解析到 DictTextResolver → 本条红。
    /// </summary>
    [Fact]
    public void ReplaceDictTextResolver_ShouldUseUserImplementation()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IDictTextResolver, FakeDictTextResolver>()),
        };
        using var scope = f.Services.CreateScope();
        Assert.IsType<FakeDictTextResolver>(scope.ServiceProvider.GetRequiredService<IDictTextResolver>());
    }

    [Fact]
    public async Task OverrideAuthStep_ShouldAffectLoginFlow()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.Replace(ServiceDescriptor.Scoped<IAuthService, OverridingAuthService>()),
        };
        var j = await (await f.CreateClient().PostJson("/api/v1/auth/login",
            new { account = "superAdmin", password = "Test@123456" })).ReadEnvelope();
        Assert.Equal("OVERRIDDEN", j.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public void ExternalAuthProvider_ShouldBePluggable()
    {
        using var f = new AdminAppFactory
        {
            Overrides = s => s.AddSingleton<IExternalAuthProvider>(new FakeExternalAuthProvider()),
        };
        // provider 是加法式扩展(TryAddEnumerable/AddSingleton 多实现按 Code 选型),消费者前置注册即并入集合
        Assert.Contains(f.Services.GetServices<IExternalAuthProvider>(), p => p.Code == "fake");
    }

    [Fact]
    public async Task DisabledModule_ShouldRemoveBuiltInController()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Dict", "Upload"] };
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/v1/sys/file/page")).StatusCode);       // 已禁 → 摘除
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/sys/user/page")).StatusCode);   // 未禁 → 仍在(需认证)
    }

    [Fact]
    public async Task CustomController_ShouldOwnSameRouteAfterModuleDisabled()
    {
        using var f = new AdminAppFactory();   // 默认禁内置 Dict → TestHost 的 CustomDictController 接管其路由
        var r = await f.CreateClient().GetAsync("/api/v1/sys/dict/type/page");
        r.EnsureSuccessStatusCode();
        var j = await r.ReadEnvelope();
        Assert.Equal("custom-dict", j.GetProperty("data").GetProperty("source").GetString());
    }

    [Fact]
    public async Task CustomSeedData_ShouldRunOnceAndBeIdempotent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"smart-seed-{Guid.NewGuid():N}.db");
        int first, second;
        // 断的是"首启种子插了 2 行",必须真从空库起,不能拿已带种子的模板副本
        using (var f1 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false, FreshDatabase = true })
        {
            _ = f1.CreateClient();                 // 触发宿主启动 → 种子运行
            first = await CountWidgets(f1);
        }
        using (var f2 = new AdminAppFactory { DbPath = dbPath, DeleteDbOnDispose = false })
        {
            _ = f2.CreateClient();                 // 同库二次启动 → 种子应幂等
            second = await CountWidgets(f2);
        }
        try { File.Delete(dbPath); } catch { /* 尽力而为 */ }

        Assert.Equal(2, first);    // 首启插入 2 行
        Assert.Equal(2, second);   // 二启仍 2 行(未重复插入)
    }

    private static async Task<int> CountWidgets(AdminAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepository<SampleWidget>>().AsQueryable().CountAsync();
    }

    /// <summary>用户自定义短信通道(替换框架默认日志通道)</summary>
    private sealed class FakeSmsSender : ISmsSender
    {
        public Task SendCodeAsync(string phone, string code, string purpose, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义邮件通道(替换框架默认日志/SMTP 通道)</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义实时推送通道(替换框架默认空实现 / 内置 SignalR)</summary>
    private sealed class FakeRealtimePublisher : IRealtimePublisher
    {
        public Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户自定义外部登录 provider(接自有 IdP,按 Code 并入 provider 集合)</summary>
    private sealed class FakeExternalAuthProvider : IExternalAuthProvider
    {
        public string Code => "fake";
        public string DisplayName => "Fake";
        public string? Icon => null;
        public Task<string> BuildAuthorizeUrlAsync(ExternalAuthorizeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult("https://idp.test/authorize");
        public Task<ExternalIdentity> ExchangeAsync(ExternalExchangeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ExternalIdentity("fake", "sub"));
    }

    /// <summary>用户自定义密码哈希(替换框架默认 PBKDF2)</summary>
    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "FAKE:" + password;
        public bool Verify(string password, string hash) => hash == "FAKE:" + password;
    }

    /// <summary>用户自定义 xlsx 读取(替换 MissingExcelProvider / MiniExcelReader)</summary>
    private sealed class FakeExcelReader : IExcelReader
    {
        public Task<IReadOnlyList<string>> ReadHeadersAsync(Stream file, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public async IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(
            Stream file, IReadOnlyDictionary<string, string> headerToKey,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>用户自定义 xlsx 写出</summary>
    private sealed class FakeExcelWriter : IExcelWriter
    {
        public Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>用户自定义模板构建</summary>
    private sealed class FakeExcelTemplateBuilder : IExcelTemplateBuilder
    {
        public Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>用户自定义导入编排(替换 ImportRunner)</summary>
    private sealed class FakeImportRunner : IImportRunner
    {
        public Task<ImportPreview> PreviewAsync(Stream file, IReadOnlyDictionary<string, string>? mapping,
            IImportProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportPreview());
        public Task<ImportPreview> ValidateAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportPreview());
        public Task<ImportCommitResult> CommitAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
            DuplicateStrategy strategy, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportCommitResult());
    }

    /// <summary>用户自定义字典 label↔value(替换 DictTextResolver)</summary>
    private sealed class FakeDictTextResolver : IDictTextResolver
    {
        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetItemsAsync(
            string dictTypeCode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
        public Task<string?> ToLabelAsync(string dictTypeCode, string? value, CancellationToken cancellationToken = default)
            => Task.FromResult(value);
        public Task<string?> ToValueAsync(string dictTypeCode, string? label, CancellationToken cancellationToken = default)
            => Task.FromResult(label);
    }

    /// <summary>用户自定义任务管理服务(替换 JobService)</summary>
    private sealed class FakeJobService : IJobService
    {
        public Task<PagedList<SysJob>> PageAsync(JobPageInput input) => throw new NotSupportedException();
        public Task<long> AddAsync(JobInput input) => throw new NotSupportedException();
        public Task UpdateAsync(long id, JobInput input) => throw new NotSupportedException();
        public Task DeleteAsync(long id) => throw new NotSupportedException();
        public Task DeleteBatchAsync(IReadOnlyCollection<long> ids) => throw new NotSupportedException();
        public Task SetEnabledAsync(long id, bool enabled) => throw new NotSupportedException();
        public Task RunOnceAsync(long id) => throw new NotSupportedException();
        public CronPreviewOutput PreviewCron(CronPreviewInput input) => new();
        public IReadOnlyList<string> ListHandlers() => [];
        public Task<JobDashboardOutput> GetDashboardAsync() => Task.FromResult(new JobDashboardOutput());
    }

    /// <summary>用户自定义处理器解析策略(替换 DefaultJobHandlerResolver)</summary>
    private sealed class FakeJobHandlerResolver : IJobHandlerResolver
    {
        public Task<IAdminJob?> ResolveAsync(string handlerName, IServiceProvider scopedProvider, CancellationToken cancellationToken = default)
            => Task.FromResult<IAdminJob?>(null);
    }

    /// <summary>用户子类化调度器(证明 TryAddSingleton + 双注册下托管的也是子类实例)</summary>
    private sealed class SubclassedScheduler(
        ISqlSugarClient db, JobExecutor executor, IEventBus eventBus, AdminJobsOptions options,
        AdminIdOptions idOptions, AdminDatabaseOptions dbOptions, IIdGenerator idGenerator, TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<JobSchedulerService> logger)
        : JobSchedulerService(db, executor, eventBus, options, idOptions, dbOptions, idGenerator, time, logger);

    /// <summary>用户覆写登录出参组装步骤(模板方法覆写)</summary>
    private sealed class OverridingAuthService(
        IRepository<SysUser> users, IPasswordHasher hasher, ITokenProvider tokens, ISessionService sessions,
        ILogService logService, ILoginLockService loginLock, ICaptchaService captcha, ISecurityPolicyProvider policy,
        ISmsOtpService smsOtp)
        : AuthService(users, hasher, tokens, sessions, logService, loginLock, captcha, policy, smsOtp)
    {
        protected override LoginOutput BuildLoginOutput(SysUser user, TokenPair pair) =>
            base.BuildLoginOutput(user, pair) with { Name = "OVERRIDDEN" };
    }

    /// <summary>用户自定义认证服务(证明 IAuthService TryAdd 可替换)</summary>
    private sealed class FakeAuthService : IAuthService
    {
        public Task<LoginOutput> LoginAsync(LoginInput input) => throw new NotSupportedException();
        public Task<LoginOutput> LoginBySmsChallengeAsync(SmsChallengeLoginInput input) => throw new NotSupportedException();
        public Task<SmsSendOutput> ResendSmsChallengeAsync(SmsResendInput input) => throw new NotSupportedException();
        public Task<LoginOutput> LoginByTotpChallengeAsync(TotpChallengeLoginInput input) => throw new NotSupportedException();
        public Task<SmsSendOutput> SendSmsLoginCodeAsync(PhoneCodeInput input) => throw new NotSupportedException();
        public Task<LoginOutput> LoginByPhoneAsync(PhoneLoginInput input) => throw new NotSupportedException();
        public Task<LoginOutput> LoginByExternalAsync(ExternalLoginInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginOutput> LoginByExternalIdentityAsync(Core.ExternalIdentity identity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LoginOutput> RefreshAsync(RefreshInput input) => throw new NotSupportedException();
        public Task LogoutAsync(string sessionId) => Task.CompletedTask;
    }

    /// <summary>用户自定义仓储(证明开放泛型 IRepository&lt;&gt; 前置注册可胜出;成员一律不实现,只验解析)</summary>
    private sealed class FakeRepository<TEntity> : IRepository<TEntity> where TEntity : AuditEntity, new()
    {
        public ISqlSugarClient Db => throw new NotSupportedException();
        public ISugarQueryable<TEntity> AsQueryable() => throw new NotSupportedException();
        public Task<TEntity?> GetByIdAsync(long id) => throw new NotSupportedException();
        public Task<TEntity?> GetFirstAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate) => throw new NotSupportedException();
        public Task<bool> AnyAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate) => throw new NotSupportedException();
        public Task<int> InsertAsync(TEntity entity) => throw new NotSupportedException();
        public Task<int> InsertRangeAsync(List<TEntity> entities) => throw new NotSupportedException();
        public Task<int> UpdateAsync(TEntity entity) => throw new NotSupportedException();
        public Task<int> DeleteAsync(long id) => throw new NotSupportedException();
        public Task<int> HardDeleteAsync(long id) => throw new NotSupportedException();
        public Task<int> RestoreAsync(long id) => throw new NotSupportedException();
    }

    /// <summary>用户自定义文件服务(证明 IFileService TryAdd 可替换)</summary>
    private sealed class FakeFileService : IFileService
    {
        public Task<FileUploadOutput> UploadAsync(FileUploadInput input) => throw new NotSupportedException();
        public Task<FileDownload> DownloadAsync(long id) => throw new NotSupportedException();
        public Task<PagedList<SysFile>> PageAsync(FilePageInput input) => throw new NotSupportedException();
        public Task DeleteAsync(long id) => Task.CompletedTask;
        public Task DeleteBatchAsync(IReadOnlyCollection<long> ids) => Task.CompletedTask;
        public Task<ChunkInitOutput> ChunkInitAsync(ChunkInitInput input) => throw new NotSupportedException();
        public Task SaveChunkAsync(ChunkSaveInput input) => throw new NotSupportedException();
        public Task<FileUploadOutput> ChunkCompleteAsync(ChunkCompleteInput input) => throw new NotSupportedException();
    }
}
