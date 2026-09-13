using SqlSugar;

namespace SmartAdmin.SqlSugar;

/// <summary>架构版本表(骨架用它验证 CodeFirst 建表 + 种子)。也是种子升级同步的版本闸门。</summary>
[SugarTable("sys_schema_version")]
public class SysSchemaVersion : BaseEntity
{
    /// <summary>
    /// 当前内核种子版本。<b>改动任何"已有种子行"(同 Id 改字段)后必须 bump</b>,
    /// 否则老库永远拿不到这次改动——种子默认只插不改,只有版本号变化才会对
    /// <see cref="ISeedData{TEntity}.SyncOnUpgrade"/> 为 true 的结构性种子执行覆盖。
    /// 新增行不需要 bump(缺哪行插哪行,本就会到)。
    /// <para>内核给自己的表<b>加列</b>时同样要 bump:它参与 CodeFirstVersion 的门控键,
    /// 不 bump 的话钉了版本号的消费方升级后不会重扫,第一次写那张表才炸在"列不存在"上。</para>
    /// </summary>
    public const string Current = "6";

    /// <summary>架构版本号,对应 <see cref="Current"/>。</summary>
    [SugarColumn(Length = 32, ColumnDescription = "架构版本")]
    public string Version { get; set; } = "";

    /// <summary>本次版本变更的应用时间。</summary>
    [SugarColumn(ColumnDescription = "应用时间")]
    public DateTime AppliedTime { get; set; }
}
