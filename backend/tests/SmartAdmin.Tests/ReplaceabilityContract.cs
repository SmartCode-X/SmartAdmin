using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 可替换性契约的扩展点清单——内核用 <c>TryAdd</c> 注册、消费者可前置注册顶掉的每一个接口。
/// <para>它同时是<b>双向</b>闸门:清单里的每一项必须真被注册且前置注册能胜出(<c>PreRegisteredExtensionPoint_ShouldWin</c>),
/// 内核注册的每一个 <c>SmartAdmin.*</c> 接口也必须出现在清单里(<c>Every_registered_interface_is_declared</c>)——
/// 新增内置服务时忘了写 TryAdd 或忘了登记,都会被后一条抓出来。</para>
/// </summary>
public static class ReplaceabilityContract
{
    /// <summary>多实现集合(<c>TryAddEnumerable</c>)不适用"前置注册顶掉"语义:它们是叠加的,不是替换。</summary>
    public static readonly Type[] MultiImplementation =
    [
        typeof(ISeedData), typeof(IAdminJob), typeof(ICaptchaProvider), typeof(IExternalAuthProvider),
        typeof(IDatabaseReadyHook),
    ];

    /// <summary>开放泛型扩展点:造不出 DispatchProxy(要先闭合),由 <c>PreRegisteredRepository_ShouldWin</c> 单测。</summary>
    public static readonly Type[] OpenGeneric = [typeof(IRepository<>)];

    /// <summary>扩展点 → 生命周期。生命周期须与内核注册一致,否则前置注册的描述符不会被 TryAdd 认作同一服务。</summary>
    public static IEnumerable<object[]> ExtensionPoints() =>
        Points.Select(p => new object[] { p.Service, p.Lifetime });

    public static readonly (Type Service, ServiceLifetime Lifetime)[] Points =
    [
        // 安全与身份
        (typeof(IPasswordHasher), ServiceLifetime.Singleton),
        (typeof(ITokenProvider), ServiceLifetime.Singleton),
        (typeof(ICurrentUser), ServiceLifetime.Singleton),
        (typeof(IDataScopeContext), ServiceLifetime.Singleton),
        (typeof(IIdGenerator), ServiceLifetime.Singleton),
        (typeof(ISecretProtector), ServiceLifetime.Singleton),
        (typeof(IDataProtectionKeyProvider), ServiceLifetime.Singleton),
        (typeof(ITotpService), ServiceLifetime.Singleton),
        (typeof(IAvatarUrlValidator), ServiceLifetime.Singleton),
        (typeof(IFileUrlSigner), ServiceLifetime.Singleton),
        (typeof(IErrorCodeCatalog), ServiceLifetime.Singleton),
        (typeof(IAuthService), ServiceLifetime.Scoped),
        (typeof(ILoginLockService), ServiceLifetime.Scoped),
        (typeof(ICaptchaService), ServiceLifetime.Scoped),
        (typeof(ISessionService), ServiceLifetime.Scoped),
        (typeof(ISessionActivityTracker), ServiceLifetime.Scoped),
        (typeof(ISecurityPolicyProvider), ServiceLifetime.Scoped),
        (typeof(IPasswordHistoryService), ServiceLifetime.Scoped),
        (typeof(IMfaPolicyService), ServiceLifetime.Scoped),
        (typeof(IMfaEnrollmentService), ServiceLifetime.Scoped),
        (typeof(IMfaChallengeService), ServiceLifetime.Scoped),
        (typeof(IReauthService), ServiceLifetime.Scoped),
        (typeof(IHighSensitivityPermissionService), ServiceLifetime.Scoped),
        (typeof(ISysUserExternalService), ServiceLifetime.Scoped),
        (typeof(IApiKeyValidator), ServiceLifetime.Singleton),

        // RBAC 与数据范围
        (typeof(IPermissionProvider), ServiceLifetime.Scoped),
        (typeof(IDataScopeProvider), ServiceLifetime.Scoped),
        (typeof(IDataScopeGuard), ServiceLifetime.Scoped),
        (typeof(IRoleGrantPolicy), ServiceLifetime.Scoped),
        (typeof(IRbacService), ServiceLifetime.Scoped),
        (typeof(IRoleService), ServiceLifetime.Scoped),

        // 基础设施
        (typeof(ICacheProvider), ServiceLifetime.Singleton),
        (typeof(IEventBus), ServiceLifetime.Singleton),
        (typeof(IRealtimePublisher), ServiceLifetime.Singleton),
        (typeof(ISmsSender), ServiceLifetime.Singleton),
        (typeof(ISmsOtpService), ServiceLifetime.Scoped),
        (typeof(IEmailSender), ServiceLifetime.Singleton),
        (typeof(IFileStorage), ServiceLifetime.Singleton),
        (typeof(IChunkStorage), ServiceLifetime.Singleton),
        (typeof(ISqlSugarClient), ServiceLifetime.Singleton),

        // 导入导出
        (typeof(IExcelReader), ServiceLifetime.Singleton),
        (typeof(IExcelWriter), ServiceLifetime.Singleton),
        (typeof(IExcelTemplateBuilder), ServiceLifetime.Singleton),
        (typeof(IDictTextResolver), ServiceLifetime.Scoped),
        (typeof(IImportRunner), ServiceLifetime.Scoped),

        // 业务服务
        (typeof(IUserService), ServiceLifetime.Scoped),
        (typeof(IOrgService), ServiceLifetime.Scoped),
        (typeof(IPositionService), ServiceLifetime.Scoped),
        (typeof(IModuleService), ServiceLifetime.Scoped),
        (typeof(IMenuService), ServiceLifetime.Scoped),
        (typeof(IDictService), ServiceLifetime.Scoped),
        (typeof(IConfigService), ServiceLifetime.Scoped),
        (typeof(INoticeService), ServiceLifetime.Scoped),
        (typeof(ILogService), ServiceLifetime.Scoped),
        (typeof(IFileService), ServiceLifetime.Scoped),
        (typeof(IMonitorService), ServiceLifetime.Scoped),
        (typeof(ICacheAdminService), ServiceLifetime.Scoped),
        (typeof(IPersonalService), ServiceLifetime.Scoped),
        (typeof(IDashboardService), ServiceLifetime.Scoped),
        (typeof(IRecycleBinService), ServiceLifetime.Scoped),

        // 定时任务
        (typeof(IJobService), ServiceLifetime.Scoped),
        (typeof(IJobLogService), ServiceLifetime.Scoped),
        (typeof(IJobHandlerResolver), ServiceLifetime.Singleton),
    ];

    /// <summary>用 <see cref="DispatchProxy"/> 现造一个接口的假实现:不必为 60 个接口各写一个 Fake 类。</summary>
    public class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;

        // .NET 10 起 Create 有多个重载,按"两个泛型参数 + 无形参"精确挑,不能用 GetMethod(name)(会 Ambiguous)
        private static readonly MethodInfo CREATE = typeof(DispatchProxy).GetMethods()
            .Single(m => m.Name == nameof(DispatchProxy.Create)
                      && m.IsGenericMethodDefinition
                      && m.GetGenericArguments().Length == 2
                      && m.GetParameters().Length == 0);

        public static object Create(Type interfaceType) =>
            CREATE.MakeGenericMethod(interfaceType, typeof(NoopProxy)).Invoke(null, null)!;
    }
}
