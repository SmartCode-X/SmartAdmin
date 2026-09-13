namespace SmartAdmin.Core;

/// <summary>
/// 种子数据主键的<b>保留区间约定</b>——内核与消费者各占一段,互不相撞。
///
/// <para><b>为什么必须有这个约定。</b>种子行的 Id 是幂等锚点,必须手工写死(见 <c>ISeedData&lt;T&gt;</c>)。
/// 内核自己就往 <c>sys_menu</c> / <c>sys_config</c> 等表里播种,消费者也会往<b>同样这些表</b>里追加自己的菜单和配置。
/// 若两边随手挑号,今天不冲突不代表明天不冲突:内核每加一个鉴权端点就要多一行菜单,号段只会往上涨——
/// 涨到消费者占用的号上,那个消费者<b>升级内核包时主键冲突、启动即崩</b>,且无法回退(他的库里已有那行)。
/// 所以这条下界是发布契约的一部分:一经发布就不能再动,改它就是破坏性变更。</para>
///
/// <code>
/// [1, 999]      内核内置种子
/// [1000, ...)   消费者种子     (上限见下)
/// </code>
///
/// <para>消费者种子的上限不是写死的整数,而是启动那一刻算出的动态雪花地板
/// <see cref="SnowflakeIdGenerator.CurrentFloor"/>(<c>DatabaseInitializer</c> 据此做运行时校验):
/// 时钟只会前进,严格小于这个值的种子 Id 从此刻起往后永远不会被本实例真实产生的雪花号撞上。
/// 这个地板远大于 4095,消费者可以用语义化的大号段编号,不必再挤在几千个连续整数里。</para>
///
/// <para>下界(内核 <see cref="KernelMax"/> / 消费者 <see cref="ConsumerMin"/>)与"内核自己不得越界"仍由
/// <c>DatabaseInitializer</c> 启动强制 + <c>SeedIdRangeTests</c> 守住。</para>
/// </summary>
public static class SmartSeedIds
{
    /// <summary>内核内置种子可用的最大 Id。内核新增种子行不得超过它(超了 <c>SeedIdRangeTests</c> 会红)。</summary>
    public const long KernelMax = 999;

    /// <summary>消费者种子可用的最小 Id。消费者的固定 Id 一律从这里起步,以免落进内核未来会用到的号段。</summary>
    public const long ConsumerMin = KernelMax + 1;

    /// <summary>
    /// 消费者种子号段的结构性上界(= <see cref="SnowflakeFloor"/> - 1),供 <c>SeedIdRangeTests</c> 等
    /// 需要一个固定闭区间上限的场景使用。运行时实际生效的种子上限是启动时刻算出的
    /// <see cref="SnowflakeIdGenerator.CurrentFloor"/>,不是这个编译期常量。
    /// </summary>
    public const long ConsumerMax = SnowflakeFloor - 1;

    /// <summary>
    /// 雪花运行时发号区的结构性下界(= <see cref="SnowflakeIdGenerator.MinId"/>):任何时刻都不可能更小的理论值,
    /// 不随时间变化。种子上限的实际校验用 <see cref="SnowflakeIdGenerator.CurrentFloor"/>,不用这个。
    /// </summary>
    public const long SnowflakeFloor = SnowflakeIdGenerator.MinId;
}
