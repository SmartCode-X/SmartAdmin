using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.TestHost;

/// <summary>
/// 示例明细表:只继承 <see cref="PrimaryId"/>(无审计四件套、无软删),照主从表里从表的常见形状。
/// 用来验证明细实体也能种子化——若 <c>ISeedData&lt;T&gt;</c> 只能用在 <c>AuditEntity</c> 上,
/// 消费者为了播几行明细数据就得给表凭空加四个审计列,或者另写一个托管服务。
/// </summary>
[SugarTable("sample_widget_detail", TableDescription = "示例明细实体(集成测试)")]
public class SampleWidgetDetail : PrimaryId
{
    [SugarColumn(ColumnDescription = "所属 widget")]
    public long WidgetId { get; set; }

    [SugarColumn(Length = 64, ColumnDescription = "明细名称")]
    public string Name { get; set; } = "";
}

/// <summary>明细表种子:验证 <c>ISeedData&lt;T&gt;</c> 在 <see cref="PrimaryId"/> 实体上可用且幂等。</summary>
public sealed class SampleWidgetDetailSeed : ISeedData<SampleWidgetDetail>
{
    public const long SeedIdA = SmartSeedIds.ConsumerMin + 10;
    public const long SeedIdB = SmartSeedIds.ConsumerMin + 11;

    public IEnumerable<SampleWidgetDetail> HasData() =>
    [
        new SampleWidgetDetail { Id = SeedIdA, WidgetId = SampleWidgetSeed.SeedIdA, Name = "detail-a" },
        new SampleWidgetDetail { Id = SeedIdB, WidgetId = SampleWidgetSeed.SeedIdA, Name = "detail-b" },
    ];
}
