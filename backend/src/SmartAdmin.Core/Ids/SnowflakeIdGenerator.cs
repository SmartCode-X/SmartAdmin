namespace SmartAdmin.Core;

/// <summary>
/// 雪花 ID 默认实现(零第三方依赖,单文件)。64 位布局:
/// <code>
/// ┌─1 bit─┬────────41 bit────────┬───6 bit───┬───6 bit───┐
/// │ 符号 0 │ 相对纪元的毫秒时间戳    │  机器号    │  毫秒内序列  │
/// └───────┴──────────────────────┴───────────┴───────────┘
/// </code>
/// <para>容量:纪元 2020-02-20 起约 69 年;64 台机器;单机单毫秒 64 个 ID,超出自旋等下一毫秒。</para>
/// <para><b>低位固定 12 bit,不要加宽</b>:41+12=53,ID 恒小于 2^53,前端按 JS 数字解析 long 主键不丢精度。
/// 换成经典 22 bit 布局,ID 从纪元起 25 天就越界。</para>
/// <para>唯一性靠两件事:进程内一把锁加毫秒内序列,进程间机器号不同。机器号由 <see cref="WorkerIdLease"/>
/// 在启动时用文件锁抢(同机进程不会同号),或由 <c>SmartAdmin:Id:WorkerId</c> 显式指定(跨机器/跨容器必须显式)。
/// <b>一个进程只能有一个实例</b>:两个实例各算各的序列,同毫秒会撞号。</para>
/// <para>时钟回拨:5ms 内自旋追平,超过直接抛异常,绝不发出可能重复的 ID。</para>
/// </summary>
public class SnowflakeIdGenerator : IIdGenerator
{
    // ── 位宽与派生常量(改位宽只动这两行,其余全部派生)──
    private const int WORKER_ID_BITS = 6;
    private const int SEQUENCE_BITS = 6;

    /// <summary>机器号上限 63。</summary>
    public const long MaxWorkerId = (1L << WORKER_ID_BITS) - 1;
    private const long SEQUENCE_MASK = (1L << SEQUENCE_BITS) - 1;        // 63
    private const int WORKER_ID_SHIFT = SEQUENCE_BITS;                   // 机器号左移 6
    private const int TIMESTAMP_SHIFT = SEQUENCE_BITS + WORKER_ID_BITS;  // 时间戳左移 12

    /// <summary>
    /// 本布局能发出的最小 ID = 2^12 = 4096,结构性下界:[1, 4095] 可放心留给固定 Id 的种子。
    /// <see cref="CurrentFloor"/> 是"从此刻起"的动态下界,两者是不同的保证。
    /// </summary>
    public const long MinId = 1L << TIMESTAMP_SHIFT;

    /// <summary>
    /// 从此刻起雪花号必然大于等于的下界(当前相对纪元毫秒数 &lt;&lt; 12)。
    /// 种子的固定 Id 只要在启动时小于它,就不会被之后发出的运行时 Id 撞上,因为时钟只往前走。纯函数,不消耗序列号。
    /// </summary>
    public static long CurrentFloor(TimeProvider? time = null) =>
        ((long)((time ?? TimeProvider.System).GetUtcNow() - EPOCH).TotalMilliseconds) << TIMESTAMP_SHIFT;

    /// <summary>可容忍的时钟回拨上限(毫秒)。NTP 微调通常在几毫秒内,超过视为异常环境。</summary>
    private const long MAX_CLOCK_DRIFT_MS = 5;

    /// <summary>
    /// 纪元 2020-02-20 02:20:02 UTC。<b>只能往更早挪,不能往后挪</b>:往后挪会与已发出的 ID 重叠。
    /// </summary>
    private static readonly DateTimeOffset EPOCH = new(2020, 2, 20, 2, 20, 2, TimeSpan.Zero);

    private readonly long _workerId;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    private long _lastTimestamp = -1;   // 上次发号的毫秒时间戳
    private long _sequence;             // 当前毫秒内已发序列

    /// <param name="workerId">机器号(0–63)。DI 路径由组合根传入:显式配置值,或 <see cref="WorkerIdLease"/> 抢到的槽位。</param>
    /// <param name="timeProvider">时间源,默认系统时钟;测试可注入假时钟。</param>
    public SnowflakeIdGenerator(long workerId = 0, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerId, MaxWorkerId);
        _workerId = workerId;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public long NextId()
    {
        lock (_lock)
        {
            var now = CurrentMillis();

            // 时钟回拨:小幅自旋追平,大幅直接拒绝
            if (now < _lastTimestamp)
            {
                var drift = _lastTimestamp - now;
                if (drift > MAX_CLOCK_DRIFT_MS)
                    throw new InvalidOperationException(
                        $"检测到时钟回拨 {drift}ms(超过容忍上限 {MAX_CLOCK_DRIFT_MS}ms),拒绝生成 ID 以避免重复。请检查服务器时钟同步。");
                now = SpinUntil(_lastTimestamp);
            }

            if (now == _lastTimestamp)
            {
                // 同一毫秒:序列自增;打满 64 个则自旋到下一毫秒重新计数
                _sequence = (_sequence + 1) & SEQUENCE_MASK;
                if (_sequence == 0)
                    now = SpinUntil(_lastTimestamp + 1);
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = now;
            return (now << TIMESTAMP_SHIFT) | (_workerId << WORKER_ID_SHIFT) | _sequence;
        }
    }

    /// <summary>当前相对纪元的毫秒数。</summary>
    private long CurrentMillis() => (long)(_time.GetUtcNow() - EPOCH).TotalMilliseconds;

    /// <summary>自旋到时间源到达目标毫秒(只在序列打满或微小回拨时进入,微秒级)。</summary>
    private long SpinUntil(long target)
    {
        long now;
        do { now = CurrentMillis(); } while (now < target);
        return now;
    }
}
