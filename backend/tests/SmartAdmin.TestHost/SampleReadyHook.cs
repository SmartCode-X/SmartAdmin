using SqlSugar;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.TestHost;

/// <summary>
/// 库就绪钩子被调用时的现场记录。<b>按宿主注册为单例</b>而非静态字段:集成测试并行跑,
/// 静态状态会被同时启动的另一个宿主覆盖(本用例就这么红过一次)。
/// </summary>
public sealed class ReadyHookRecord
{
    /// <summary>钩子收到的上下文;没被调用过则为 null。</summary>
    public DatabaseReadyContext? Context { get; set; }

    /// <summary>调用时刻库里的 widget 行数——种子播 2 行,晚于种子调用才会看到 2。</summary>
    public int WidgetCount { get; set; }
}

/// <summary>
/// 示例库就绪钩子——消费者"等表建好、种子播完再初始化自己的数据"的正规接法。
/// </summary>
public sealed class SampleReadyHook(ISqlSugarClient db, ReadyHookRecord record) : IDatabaseReadyHook
{
    public async Task OnDatabaseReadyAsync(DatabaseReadyContext context, CancellationToken cancellationToken)
    {
        record.Context = context;
        record.WidgetCount = await db.Queryable<SampleWidget>().CountAsync();
    }
}
