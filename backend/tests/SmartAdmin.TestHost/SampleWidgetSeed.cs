using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.TestHost;

/// <summary>
/// 示例"用户自定义种子"——固定 Id 保证幂等。
/// <para>Id 从 <see cref="SmartSeedIds.ConsumerMin"/> 起取:这是消费者该守的区间(内核占低号段,雪花占高号段)。
/// 这个类是对外的抄写范本,别让它示范"随手挑个整数"。</para>
/// </summary>
public sealed class SampleWidgetSeed : ISeedData<SampleWidget>
{
    public const long SeedIdA = SmartSeedIds.ConsumerMin;
    public const long SeedIdB = SmartSeedIds.ConsumerMin + 1;

    public IEnumerable<SampleWidget> HasData() =>
    [
        new SampleWidget { Id = SeedIdA, Name = "widget-a" },
        new SampleWidget { Id = SeedIdB, Name = "widget-b" },
    ];
}
