using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 独立 Worker 进程的组合根——纯 Generic Host、无 ASP.NET 的装配路径。
/// <para><b>绝大多数消费者用不到这个</b>:<c>AddSmartAdmin()</c> 的默认形态下,调度器就跑在 API 进程内,
/// 多副本靠 DB 选主自动互备。只有想要「API 停了任务照跑」或把任务负载隔离出 API 进程时,才另起一个 Worker 进程
/// (照抄 <c>samples/WorkerHost</c>,Program.cs 同样只有几行)。</para>
/// <para>选项 POCO 与 API 宿主共用 <see cref="SmartAdminOptionsSetup.AddSmartAdminOptions"/> 入容器;
/// 本方法只多做 Worker 特有的机器号强校验,再接同一条数据层 + 领域服务装配链。</para>
/// </summary>
public static class WorkerSetup
{
    /// <summary>
    /// 装配一个只跑调度器的 Worker:绑定 <c>SmartAdmin</c> 配置节 → 注册 Services 层依赖的各选项 POCO →
    /// 数据层 + 领域服务(其中就包含 <see cref="JobSchedulerService"/> 的托管注册)。
    /// </summary>
    /// <param name="services">宿主的服务集合</param>
    /// <param name="configuration">宿主配置(读 <c>SmartAdmin</c> 节)</param>
    /// <param name="configure">代码侧覆写(在绑定之后、注册之前生效)</param>
    /// <exception cref="InvalidOperationException">
    /// 未显式配置 <c>SmartAdmin:Id:WorkerId</c> 时直接抛——Worker 天然是多实例形态(至少与一个 API 副本同时在跑),
    /// 雪花机器号同号会让不同进程在同毫秒发出相同 Id、撞主键。这比 API 侧的 Redis 守卫更严:API 可能真是单实例,Worker 不可能。
    /// </exception>
    public static IServiceCollection AddSmartAdminWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SmartAdminOptions>? configure = null)
    {
        // 重复装配短路(同 AddSmartAdmin):再跑一遍只会把调度器、文件回收托管成两份
        if (services.Any(d => d.ServiceType == typeof(SmartAdminRegistered))) return services;

        var options = new SmartAdminOptions();
        configuration.GetSection("SmartAdmin").Bind(options);
        configure?.Invoke(options);

        if (options.Id.WorkerId is null)
            throw new InvalidOperationException(
                "Worker 进程必须显式配置 SmartAdmin:Id:WorkerId(0–63,与所有 API 副本及其它 Worker 互不相同)。" +
                "Worker 天然是多实例形态,机器号同号会让不同进程在同毫秒发出相同的雪花 Id、撞主键。");
        // 与 AddSmartAdmin 共用:租约、正数项、HTTP 围栏 CIDR——Worker 才是真正执行任务的一侧,不能漏
        AdminJobsOptionsValidation.Validate(options.Jobs);

        services.TryAddSingleton<SmartAdminRegistered>();
        services.TryAddSingleton(options.Id);   // 机器号已在上面强校验过,直接入容器(API 宿主是抢号工厂,故不在共用助手里)
        services.AddSmartAdminOptions(options);

        // 与 AddSmartAdmin 对齐:实体扫描 = 内置 Services + 消费者 ApplicationAssemblies。
        // 漏挂 Services 时，若运维把 Worker 的 EnableCodeFirst 打开，只会建出 SqlSugar 层的 sys_schema_version，
        // 任务/用户等内核表缺失，调度器一启动就查无表。
        var entityAssemblies = new List<Assembly> { typeof(ServicesSetup).Assembly };
        entityAssemblies.AddRange(options.ApplicationAssemblies);
        services.AddSmartAdminSqlSugar(options.Database, [.. entityAssemblies.Distinct()], options.AdditionalDatabases);
        services.AddSmartAdminServices();
        return services;
    }
}
