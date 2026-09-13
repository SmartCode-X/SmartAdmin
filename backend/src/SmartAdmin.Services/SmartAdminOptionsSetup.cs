using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 选项 POCO 入容器的唯一出口,API 宿主与 Worker 宿主共用。
/// <para>一律 <c>TryAddSingleton</c>:消费者在 <c>AddSmartAdmin()</c> 之前注册同类型实例即整体覆盖,
/// 与内核其它服务同一条可替换性契约;重复装配也不会注册出第二份。</para>
/// <para>不含 <see cref="AdminIdOptions"/>:API 宿主未配机器号时要抢文件锁、Worker 宿主必须显式配置,两边策略不同,各自注册。</para>
/// </summary>
public static class SmartAdminOptionsSetup
{
    /// <summary>把 <paramref name="options"/> 下各节选项 POCO 以 <c>TryAddSingleton</c> 注册进容器,并登记消费者的错误码枚举。</summary>
    public static IServiceCollection AddSmartAdminOptions(this IServiceCollection services, SmartAdminOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton(options.Database);
        services.TryAddSingleton(options.Cache);
        services.TryAddSingleton(options.Seed);
        services.TryAddSingleton(options.Jwt);
        services.TryAddSingleton(options.Security);
        // 上传根按 ContentRoot 解析(与 SQLite 库文件、JWT 开发密钥同一基准),不按进程 CWD。
        // 三条可写路径若基准不一致,容器/服务托管里 CWD 一变(entrypoint 脚本 cd 过、k8s 覆写 workingDir、
        // 从别处 `dotnet /app/App.dll`),数据卷和上传卷就悄悄分家:库还在,文件"消失"。绝对路径原样保留。
        // 就地改写同一个 options.Upload 实例,故 SmartAdminOptions.Upload 读到的也是解析后的值;
        // 裸容器(没有宿主环境)按原值使用。
        services.TryAddSingleton(sp =>
        {
            if (sp.GetService<IHostEnvironment>() is { } env)
                options.Upload.RootPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Upload.RootPath));
            return options.Upload;
        });
        services.TryAddSingleton(options.Api);
        services.TryAddSingleton(options.Email);
        services.TryAddSingleton(options.ExternalAuth);
        services.TryAddSingleton(options.Realtime);
        services.TryAddSingleton(options.Excel);
        services.TryAddSingleton(options.Jobs);
        services.TryAddSingleton(options.Logging);

        // 分页上限是静态的(扩展方法拿不到 DI),在此从配置写入
        if (options.Api.MaxPageSize <= 0)
            throw new InvalidOperationException("SmartAdmin:Api:MaxPageSize 必须大于 0。");
        PagedListExtensions.MaxSize = options.Api.MaxPageSize;

        RegisterErrorCodes(options);
        return services;
    }

    /// <summary>
    /// 登记消费者的错误码枚举:显式列出的 <see cref="SmartAdminOptions.ErrorCodeEnums"/>,
    /// 外加 <see cref="SmartAdminOptions.ApplicationAssemblies"/> 里带 <c>[MsgKey]</c> 的枚举(自动扫描)。
    /// 登记后 <c>(ErrorCode)MyCodes.X</c> 抛出的异常,信封里的 msgKey 就是该成员自己的键。
    /// </summary>
    private static void RegisterErrorCodes(SmartAdminOptions options)
    {
        foreach (var type in options.ErrorCodeEnums) ErrorCodeRegistry.Register(type);

        foreach (var assembly in options.ApplicationAssemblies.Distinct())
        {
            Type[] types;
            // 业务程序集引用不全时 GetTypes 会部分失败,拿能拿到的那部分即可,不该因此起不来
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t is not null)!]; }

            foreach (var type in types)
            {
                if (!type.IsEnum) continue;
                if (!type.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Any(f => f.IsDefined(typeof(MsgKeyAttribute), inherit: false))) continue;
                ErrorCodeRegistry.Register(type);
            }
        }
    }
}
