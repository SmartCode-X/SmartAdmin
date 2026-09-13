namespace SmartAdmin.Core;

/// <summary>导出档案:一个实体"能导哪些列"的声明。</summary>
public interface IExportProfile
{
    /// <summary>本档案的唯一编码(导出端点按它选择档案)。</summary>
    string Code { get; }

    /// <summary>该档案声明的可导出列全集(实际导出列受用户勾选与 <see cref="ExportColumn.DefaultSelected"/> 约束)。</summary>
    IReadOnlyList<ExportColumn> Columns { get; }
}
