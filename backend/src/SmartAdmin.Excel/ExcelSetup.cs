using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.Core;

namespace SmartAdmin.Excel;

/// <summary>
/// Excel 编解码装配(可选包入口)。在 <c>AddSmartAdmin()</c> <b>之前</b>调用即前置注册
/// <see cref="IExcelReader"/> / <see cref="IExcelWriter"/> / <see cref="IExcelTemplateBuilder"/>
/// 的真实现,压过内核默认的 <see cref="MissingExcelProvider"/>(内核用 <c>TryAdd</c> 注册,先到者胜)。
/// <para>不装本包 / 不调本方法 → 任意 codec 调用一律抛 <see cref="ErrorCode.ExcelProviderMissing"/>(46001),fail-loud。</para>
/// </summary>
public static class ExcelSetup
{
    /// <summary>
    /// 启用 xlsx 读写与模板生成:注册 MiniExcel 读/写 + OpenXml 模板构建。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSmartAdminExcel(); // 先注册,赢 TryAdd
    /// builder.Services.AddSmartAdmin(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddSmartAdminExcel(this IServiceCollection services)
    {
        // 晚于内核调用 = 三个 TryAdd 全部落空,直到第一次导入/导出才抛 46001;这里在装配期就把顺序错报出来。
        if (services.Any(d => d.ServiceType == typeof(SmartAdminRegistered)))
            throw new InvalidOperationException(
                "AddSmartAdminExcel() 必须在 AddSmartAdmin() 之前调用:内核已用 TryAdd 注册了默认的 MissingExcelProvider,后注册的实现不会生效(导入/导出会一律抛 46001)。");

        // TryAdd:与内核可替换性模型一致——本方法须在 AddSmartAdmin() 之前调用方能胜出。
        services.TryAddSingleton<IExcelReader, MiniExcelReader>();
        services.TryAddSingleton<IExcelWriter, MiniExcelWriter>();
        services.TryAddSingleton<IExcelTemplateBuilder, OpenXmlTemplateBuilder>();
        return services;
    }
}
