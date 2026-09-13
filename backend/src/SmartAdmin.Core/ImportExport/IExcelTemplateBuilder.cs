namespace SmartAdmin.Core;

/// <summary>导入模板生成(codec 层)。字典列要出真下拉,故此实现走 OpenXml 而非 MiniExcel。</summary>
public interface IExcelTemplateBuilder
{
    /// <summary>按模板规格生成一个可下载的 xlsx 模板文件。</summary>
    Task<Stream> BuildAsync(TemplateSpec spec, CancellationToken cancellationToken = default);
}
