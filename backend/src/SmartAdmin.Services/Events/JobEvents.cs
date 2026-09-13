namespace SmartAdmin.Services;

/// <summary>
/// 任务变更事件(增/删/改/启停后发布)。调度循环订阅它即刻唤醒重载——但 <c>ChannelEventBus</c>
/// 是进程内总线,别的副本收不到;跨副本收敛靠调度循环的周期重载兜底(ReloadSeconds,
/// 这是唤醒三通道之一)。
/// </summary>
public record JobChangedEvent(long JobId);
