using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 用户侧入口:Program.cs 里调用 <c>AddSmartAdmin()</c> + <c>MapSmartAdmin()</c> 即起全站。
/// <para>认证/授权中间件无需用户手动 Use——WebApplication 检测到认证服务注册后自动插入管道。</para>
/// </summary>
public static class SmartAdminSetup
{
    /// <summary>内置 CORS 命名策略名(由 <see cref="SmartAdminMiddlewareStartupFilter"/> 在管道前段应用)</summary>
    public const string CorsPolicyName = "SmartAdmin";

    /// <summary>
    /// 注册 SmartAdmin 全站服务(数据层、领域服务、认证授权、内置控制器、过滤器等)。
    /// </summary>
    /// <param name="services">宿主的服务集合。</param>
    /// <param name="configuration">承载 <c>SmartAdmin</c> 配置节的根配置;缺省节等价于全部走默认值。</param>
    /// <param name="configure">代码覆写钩子,在配置绑定之后执行,可再改任意选项。</param>
    public static IServiceCollection AddSmartAdmin(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SmartAdminOptions>? configure = null)
    {
        // 重复装配短路:第一次已把托管服务、过滤器、认证全挂上,再跑一遍只会注册出第二份托管服务(调度器、文件回收各起两个)。
        if (services.Any(d => d.ServiceType == typeof(SmartAdminRegistered))) return services;

        // ── 配置:json 绑定(缺省即默认值,零配置可跑)→ 代码覆写 → Options 对象直接入容器 ──
        var options = new SmartAdminOptions();
        var section = configuration.GetSection("SmartAdmin");
        section.Bind(options);
        configure?.Invoke(options);

        // 生产环境读不到配置节又没有代码覆写 = 全部落默认值:SQLite、随机超管密码、机器号抢号、JWT 走生产缺密钥的抛错。
        // 前三项都是静默的(消费者改漏节名时真实发生过),启动即拒比事后发现库跑在 SQLite 上便宜得多。
        if (configure is null && !section.Exists() && IsProduction(services))
            throw new InvalidOperationException(
                "生产环境未找到 SmartAdmin 配置节:appsettings 里没有 \"SmartAdmin\" 节(或环境变量前缀 SmartAdmin__)," +
                "也没有用 AddSmartAdmin(config, configure) 从代码覆写,数据库/超管密码/机器号将全部落默认值。请检查节名。");

        // 若配置了 Security:Profile 或 Security:Level3,说明用的是把 TOTP、Cookie 会话与一组策略地板
        // 收在一起的总档式配置形状,需要迁移到下面这些独立开关。这里不静默忽略,是因为忽略就会把这些
        // 安全项一并关掉,且关掉的那一刻没有任何迹象。
        var legacyProfile = section["Security:Profile"];
        if (!string.IsNullOrWhiteSpace(legacyProfile) || section.GetSection("Security:Level3").GetChildren().Any())
            throw new InvalidOperationException(
                "SmartAdmin:Security:Profile 与 SmartAdmin:Security:Level3 已移除,请改配独立开关:" +
                "TOTP 用 Security:Totp:Enabled(Issuer / ChallengeTtlSeconds / ReauthWindowMinutes 同节);" +
                "Cookie 会话用 Security:Session:CookieMode(及 CookieDomain);" +
                "会话闲置与绝对寿命用 Security:Session:IdleMinutesNormal / IdleMinutesMfa / AbsoluteHours;" +
                "口令强度、历史、有效期与登录锁定改在配置中心「安全策略」里设。");

        services.TryAddSingleton<SmartAdminRegistered>();
        services.AddSmartAdminOptions(options);
        // 定时任务:API 与 Worker 共用同一校验入口(租约/正数项/CIDR 围栏)
        AdminJobsOptionsValidation.Validate(options.Jobs);

        // ── 雪花机器号:多副本同号 = 同毫秒发号撞主键。这是数据损坏级的问题,且不会有任何报错提示 ──
        //   真正的来源不是算法:同一份配置被同机多个进程同时加载(IIS 应用池重叠回收期间新旧 w3wp 并存、Web 园、
        //   同机多份部署),各进程拿同一个号各算各的序列。所以未显式配置时不回落 0,而是启动时用文件锁抢一个
        //   本机空闲槽位(WorkerIdLease):旧进程占着 0,新进程自动落到 1,结构上不可能同号,不依赖任何人配对。
        //   文件锁管不到跨机器/跨容器(各自文件系统独立)。没有可靠的"我是不是多副本"信号,但选了 Redis 缓存基本等同于
        //   宣告多实例意图(进程内缓存在多副本下根本不成立:强退失效、权限陈旧、锁定计数翻倍)。
        //   故 Redis + 未显式给机器号 → 启动即抛。显式写 0 即视为知情,放行。
        if (string.Equals(options.Cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase) && options.Id.WorkerId is null)
            throw new InvalidOperationException(
                "已配置 Redis 缓存(多实例部署),但未显式设置 SmartAdmin:Id:WorkerId。" +
                "跨机器/跨容器水平扩展时每个实例必须配一个互不相同的机器号(0–63),否则不同实例同毫秒发号会撞主键。" +
                "单实例请显式配 0 以示知情;k8s 可用 StatefulSet 的 Pod 序号注入。");
        if (options.Id.WorkerId is null)
        {
            // 工厂注册而非实例注册:容器只 Dispose 自己创建的单例,而槽位必须随宿主释放(测试里成百个宿主起停)。
            // 首次解析 AdminIdOptions 时才真正抢锁并回写,与上面 Upload.RootPath 的解析期回写同一路数;
            // 此后发号器、任务节点名、文件日志后缀、数据库租约守卫读到的都是同一个有效机器号。
            services.AddSingleton(_ => WorkerIdLease.Acquire(options.Id.WorkerIdLockDir));
            services.AddSingleton(sp =>
            {
                options.Id.WorkerId ??= (int)sp.GetRequiredService<WorkerIdLease>().WorkerId;
                return options.Id;
            });
        }
        else
        {
            services.AddSingleton(options.Id);
        }
        services.TryAddSingleton(TimeProvider.System);          // 统一时间源,测试可换 Fake

        // ── 文件日志:默认关。开了才长出 logs/{级别}/{日期}.log ──────────────────
        //   ILogger 默认只有 Console provider,进程一重启异常堆栈就没了;而运行时依赖红线(只准 SqlSugarCore + Microsoft.*)
        //   把 Serilog 挡在外面,故内核自带一个(FileLoggerProvider)。它是加法不排他:消费者照常 AddSerilog(),两者并存。
        if (options.Logging.File.Enabled)
        {
            // 两个泛型参数缺一不可:TryAddEnumerable 要从工厂的返回类型认出实现类型才能去重,
            // 只写 <ILoggerProvider> 会因"与其他 ILoggerProvider 注册无从区分"在启动时抛。
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(sp =>
            {
                var env = sp.GetRequiredService<IWebHostEnvironment>();
                // 与上传根、SQLite 库文件同一基准:相对路径按 ContentRoot 解析,不随进程 CWD 漂移
                var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Logging.File.Path));

                // 红线:日志落在静态根下 = UseStaticFiles() 把异常堆栈、请求参数、内部路径匿名直出。
                // 同 Upload.RootPath 的老坑,但那个至少还要猜文件名——日志的路径是可枚举的。宁可启动就炸。
                //
                // WebRootPath 在 wwwroot **目录尚不存在**时是 null —— 直接信它,守卫就会在最该管用的时候失灵:
                // 全新部署的机器上 wwwroot 往往还没有,于是守卫跳过、日志照写进 wwwroot/logs;等第一次上传时
                // LocalFileStorage 把 wwwroot 建出来,静态中间件就开始把整个日志目录匿名直出。
                // 所以判据是「这个路径会不会落进静态根」,而不是「静态根现在存不存在」:目录缺失时回退到默认的 wwwroot 名。
                var webRoot = string.IsNullOrEmpty(env.WebRootPath)
                    ? Path.Combine(env.ContentRootPath, "wwwroot")
                    : env.WebRootPath;
                if (root.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"SmartAdmin:Logging:File:Path 解析到 {root},位于 wwwroot 内。" +
                        "宿主一旦 UseStaticFiles() 托管前端产物,整个日志目录就会被匿名直出(异常堆栈、请求参数、内部路径全在里面)。请把它挪出静态根。");

                // 机器号走 DI 取有效值(自动抢号的结果在首次解析 AdminIdOptions 时回写),直接读 options.Id 可能还是 null
                return new FileLoggerProvider(root, options.Logging.File, sp.GetRequiredService<AdminIdOptions>().WorkerId ?? 0,
                    sp.GetRequiredService<TimeProvider>());
            }));
        }

        // ── 当前用户 + 数据范围环境:HTTP 侧实现在此先注册,压过 SqlSugar 层的 AsyncLocal 兜底 ──
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ICurrentUser, HttpContextCurrentUser>();
        // HttpContext.Items 版数据范围载体(避免授权过滤器里 AsyncLocal 不回流的陷阱);非 HTTP 场景回退 AsyncLocal
        services.TryAddSingleton<IDataScopeContext, HttpContextDataScopeContext>();

        // ── 数据层 + 领域服务(实体程序集在此登记)──────────────
        //   实体扫描 = 内置 Services + 用户显式登记的业务程序集,让用户实体也 CodeFirst 建表
        var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
        entityAssemblies.AddRange(options.ApplicationAssemblies);
        services.AddSmartAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);

        // ── 实时通知:开启时挂 SignalR + 注册真实现,压过 Services 的 Noop(TryAdd 先到者胜,故须在 AddSmartAdminServices 之前)──
        //   SignalR 属 ASP.NET Core 共享框架,零新增 NuGet。关闭时不挂 Hub、不建长连接,Noop 生效 = 维持既有轮询/惰性 401(纯增强)。
        if (options.Realtime.Enabled)
        {
            services.AddSignalR();
            services.TryAddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        }

        services.AddSmartAdminServices();

        // ── JWT:签名密钥惰性解析一次(生产缺配 fail-fast;开发密钥持久化 + 警告),签发与验证共用同一实例 ──
        services.TryAddSingleton(sp =>
            JwtKeyResolver.Resolve(options.Jwt,
                sp.GetRequiredService<IHostEnvironment>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtKeyResolver))));
        services.TryAddSingleton<ITokenProvider>(sp => new JwtTokenProvider(
            options.Jwt, sp.GetRequiredService<SymmetricSecurityKey>(), sp.GetRequiredService<TimeProvider>()));

        // 文件直链签名:从 JWT 密钥派生子密钥,签出匿名可访问但不可伪造的 /view 链接
        //(<img src> 带不了 Authorization 头 —— 通知正文里的图片全靠它)
        services.TryAddSingleton<IFileUrlSigner>(sp => new FileUrlSigner(
            sp.GetRequiredService<SymmetricSecurityKey>(),
            sp.GetRequiredService<AdminUploadOptions>(),
            sp.GetRequiredService<TimeProvider>()));

        // 头像 URL 校验:依赖上面刚注册的 IFileUrlSigner,故放在这层而非 ServicesSetup——
        // 纯 Services 宿主(WorkerSetup)没有 IFileUrlSigner,PersonalService/UserService 拿到的是可选依赖,
        // 未注册时就是 null(跳过校验),不会在那种宿主里因缺依赖而 DI 解析失败。
        services.TryAddSingleton<IAvatarUrlValidator, AvatarUrlValidator>();

        // 权限码提供者由 Services 层的 RbacPermissionProvider 提供(RBAC 真实现);
        // 用户前置注册同接口实现(如对接外部鉴权中心)即整体替换。

        // ── 认证/授权:微软官方 JwtBearer ────────────────────────
        // 默认 scheme 仍是 Bearer;API Key 是第二个 scheme,只有端点显式挂 [ApiKey] 才用到,JWT 路径一字不变
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);
        // 验证参数经 Options 框架注入签名密钥——与签发端共享同一单例,无静态桥
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SymmetricSecurityKey>((o, signingKey) =>
            {
                o.MapInboundClaims = false;                     // 保留原始 claim 名(sub/sid/sadm),不做遗留映射
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Jwt.Issuer,
                    IssuerSigningKey = signingKey,
                    ValidateAudience = false,                   // 单体管理后台,不启用 audience 维度
                    ValidateLifetime = true,                    // 显式:校验 exp/nbf
                    ClockSkew = TimeSpan.FromSeconds(30),       // 收紧默认 5 分钟宽限,贴合短命令牌策略
                    NameClaimType = JwtRegisteredClaimNames.UniqueName, // User.Identity.Name = 登录账号(走常量,不写死字面量)
                };
                // 未认证/令牌过期的框架 401 challenge 也套统一信封(40006),与 [RolePermission]/[ActiveSession] 一致
                o.Events = new JwtBearerEvents
                {
                    // SignalR 的浏览器客户端(WebSocket)带不了 Authorization 头,令牌走 query(access_token);
                    // 仅在实时 Hub 路径上采信该 query 令牌,不放宽普通 API 端点的取令牌方式。
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments(options.Realtime.HubPath))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    },
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.TokenInvalid));
                    },
                };
            });
        // 默认拒绝走 MapControllers().RequireAuthorization()(见 MapSmartAdmin),只作用于真实控制器端点、
        // 尊重 [AllowAnonymous],且不影响未匹配路由的 404(FallbackPolicy 会把 404 劫持成 401,故不用它)。
        services.AddAuthorization();

        // ── 外部登录 / SSO:按 appsettings 装内置 OIDC provider(零新包);未配则整段跳过 ──
        services.AddExternalAuthProviders(options.ExternalAuth);

        // ── MVC 控制器:本程序集作为 ApplicationPart 挂入宿主 ──
        //   全局过滤器:业务异常 → 统一信封;操作日志(默认记一切写操作,读操作/匿名端点除外);
        //   裸返回兜底包信封(用户控制器 return dto 即得 Result<T>)
        var mvc = services.AddControllers(o =>
            {
                if (options.DemoMode)
                    o.Filters.Add<DemoModeFilter>();
                // 数据范围先于一切:已认证但没挂授权特性的端点(消费者只写 [Authorize] 的那种)也拿到真实范围,
                // 否则会落到 HttpContextDataScopeContext 的 fail-closed 回退、查不到任何受控行。
                o.Filters.Add<DataScopeAutoBindFilter>(int.MinValue);
                o.Filters.Add<AdminExceptionFilter>();
                o.Filters.Add<ExceptionLogFilter>();   // 未捕获异常旁路留痕(不吞异常,500 照旧);业务异常显式跳过
                o.Filters.Add<OperationLogFilter>();
                o.Filters.Add<ResultEnvelopeFilter>();
                o.Conventions.Add(new DisabledModuleConvention(options.Api));   // 按配置摘除禁用模块的控制器
            })
            .AddApplicationPart(typeof(SmartAdminSetup).Assembly);   // 内置控制器
        // 用户业务程序集里的控制器也挂进来,与内置控制器同管道
        foreach (var assembly in options.ApplicationAssemblies.Distinct())
            mvc.AddApplicationPart(assembly);

        // ── CORS:命名策略,默认收紧(无源=不放行);经 IStartupFilter 挂载,零配置宿主无需手动 UseCors ──
        services.AddCors(o => o.AddPolicy(CorsPolicyName, p =>
        {
            if (options.Api.Cors.AllowedOrigins.Length > 0)
            {
                p.WithOrigins(options.Api.Cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
                if (options.Api.Cors.AllowCredentials) p.AllowCredentials();
            }
            // 空源 → 空策略:不放行任何跨源(生产必须显式配置)
        }));
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, SmartAdminMiddlewareStartupFilter>());

        // ── 反向代理转发头:反代之后 RemoteIpAddress 是代理的 IP,不解析 XFF 则限流按 IP 分区形同虚设 ──
        //   安全红线:开了却不声明受信来源 = 采信任何人伪造的 X-Forwarded-For = 每个伪造 IP 开一个新分区 → 限流被完全绕过。
        //   宁可启动就炸,也不静默留一个可绕过的限流器(同 JwtKeyResolver 生产缺密钥的 fail-fast 成法)。
        var fwd = options.Api.ForwardedHeaders;
        if (fwd.Enabled)
        {
            if (fwd.KnownProxies.Length == 0 && fwd.KnownNetworks.Length == 0)
                throw new InvalidOperationException(
                    "已启用 SmartAdmin:Api:ForwardedHeaders,但未声明任何受信来源。请配置 KnownProxies(代理 IP)" +
                    "或 KnownNetworks(CIDR 网段,容器编排下更实际,如 172.16.0.0/12)。" +
                    "无条件采信 X-Forwarded-For 会让攻击者用伪造 IP 绕过限流并污染审计日志。");

            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                   | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = fwd.ForwardLimit;
                // 框架默认信回环(::1/127.0.0.1);容器里代理不是回环 → 清空后只信用户显式声明的来源。
                o.KnownProxies.Clear();
                o.KnownIPNetworks.Clear();
                foreach (var ip in fwd.KnownProxies) o.KnownProxies.Add(IPAddress.Parse(ip));
                foreach (var cidr in fwd.KnownNetworks) o.KnownIPNetworks.Add(IPNetwork.Parse(cidr));
            });
        }

        // ── 限流:按客户端 IP 固定窗口,认证端点(/api/v1/auth/*)更严;经上面的 IStartupFilter 挂中间件 ──
        //   计数走 ICacheProvider.IncrementAsync ⇒ 装 Redis 即跨副本共享(否则 N 个副本 = N × 阈值,爆破基线被静默削半)。
        //   实现与取舍见 RateLimitMiddleware 的类注释(为何不用 ASP.NET 的 PartitionedRateLimiter)。
        services.TryAddSingleton<RuntimeRateLimit>();
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeRateLimit>());

        services.TryAddSingleton<AuthCookieService>(); // Cookie/CSRF 服务;启用条件见会话选项

        // ── 内置 OpenAPI 文档(契约源)+ 健康检查(/health 存活 + /health/ready 依赖就绪)──
        // 产出 /openapi/v1.json,补三件默认生成器不给的东西:稳定的 operationId(不然同一资源的多个
        // Delete/Update 全靠路径区分,前端代码生成器认不出该起什么方法名)、每个端点要什么权限、以及错误码目录。
        services.AddOpenApi(o =>
        {
            o.AddOperationTransformer<SmartAdminOperationTransformer>();
            o.AddDocumentTransformer<ErrorCodeDocumentTransformer>();
        });
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"])
            .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// 装配期判断宿主是否生产环境:WebApplicationBuilder / Host.CreateApplicationBuilder 都把 <see cref="IHostEnvironment"/>
    /// 以实例注册进集合,不用建容器就能读;裸容器(测试、只装数据层的消费者)没有它,按非生产处理。
    /// </summary>
    private static bool IsProduction(IServiceCollection services) =>
        services.FirstOrDefault(d => d.ServiceType == typeof(IHostEnvironment))?.ImplementationInstance is IHostEnvironment env
        && env.IsProduction();

    /// <summary>
    /// 映射 SmartAdmin 的端点:内置控制器路由(默认拒绝,<c>[AllowAnonymous]</c> 显式豁免)、
    /// 实时通知 Hub(若已开启)、开发环境的 OpenAPI 文档、健康检查。
    /// </summary>
    public static IEndpointRouteBuilder MapSmartAdmin(this IEndpointRouteBuilder endpoints)
    {
        // 内置控制器路由(认证、探针;后续模块的控制器自动包含)。
        // 默认拒绝:所有控制器端点强制认证,[AllowAnonymous](登录/刷新/验证码/自定义匿名控制器)显式豁免;
        // 漏挂 [RolePermission] 的 action 因此也强制走认证,不会被静默公开。只作用于真实端点,不劫持未匹配路由的 404。
        endpoints.MapControllers().RequireAuthorization();

        // 实时通知 Hub:仅在开启时映射。Hub 自带 [Authorize],不受上面 MapControllers 的 RequireAuthorization 影响;
        // JWT 走 query access_token(见 AddSmartAdmin 的 OnMessageReceived)。关闭时不映射,前端连接失败即退回轮询兜底。
        var realtime = endpoints.ServiceProvider.GetService<AdminRealtimeOptions>();
        if (realtime?.Enabled == true)
            endpoints.MapHub<SmartHub>(realtime.HubPath);

        // OpenAPI 文档:仅开发环境暴露(生产匿名开放会泄露完整 API 契约作侦察面);
        // 匿名可访问(否则被上面的 FallbackPolicy 挡成 401)。契约源本就是开发期前端代码生成用。
        var env = endpoints.ServiceProvider.GetService<IHostEnvironment>();
        if (env is null || env.IsDevelopment())
            endpoints.MapOpenApi().AllowAnonymous();

        // 健康检查:/health 只报进程存活(不跑依赖检查),/health/ready 探 DB/缓存就绪。
        // 匿名(供编排层 liveness/readiness 探针),不受默认拒绝约束。
        // 正文是 JSON(状态码不变):默认写手只吐一个 Healthy 字面串,探针够用,人不够用——
        // ready 变红时看不出是数据库还是缓存,更看不出慢在哪一项。
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();
        return endpoints;
    }
}
