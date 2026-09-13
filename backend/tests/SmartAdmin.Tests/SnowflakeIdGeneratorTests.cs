using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 雪花 ID 布局。核心不变量:生成的 long 主键必须恒 &lt; 2^53(JS Number.MAX_SAFE_INTEGER),
/// 否则前端按数字解析 long 会丢精度——低位若用 22 bit(经典 Twitter 布局),ID 从纪元起 25 天就越界,
/// 角色授权保存等携带新建 ID 的请求静默失败。低位固定 12 bit(机器 6 + 序列 6)锁死此保证。
/// </summary>
public class SnowflakeIdGeneratorTests
{
    private const long JsMaxSafeInteger = 1L << 53; // 9,007,199,254,740,992

    /// <summary>固定时钟,只为把发号时间推到纪元寿命的两端做边界断言(避免引入 FakeTimeProvider 包)。</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Ids_today_are_js_safe_and_unique()
    {
        var gen = new SnowflakeIdGenerator(workerId: 63); // 最大机器号,低位取满
        var seen = new HashSet<long>();
        for (var i = 0; i < 20_000; i++)
        {
            var id = gen.NextId();
            Assert.True(id > 0 && id < JsMaxSafeInteger, $"ID {id} 越过 2^53");
            Assert.True(seen.Add(id), $"ID {id} 重复");
        }
    }

    [Fact]
    public void Ids_stay_js_safe_at_end_of_epoch_lifetime()
    {
        // 纪元 2020-02-20 + ~69 年,接近 41 位毫秒上限(约 2089-10 到顶);此处 ID 最接近 2^53,是最严苛的边界
        var nearEnd = new DateTimeOffset(2089, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var gen = new SnowflakeIdGenerator(workerId: 63, timeProvider: new FixedTime(nearEnd));
        var id = gen.NextId();
        Assert.True(id < JsMaxSafeInteger, $"纪元末 ID {id} 越过 2^53——低位被加宽了?");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)] // 6 bit 机器号上限为 63
    public void Worker_id_out_of_range_throws(long workerId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(workerId));
    }

    [Fact]
    public void Concurrent_callers_at_the_same_instant_never_collide()
    {
        // "高并发同一时刻重号"的进程内一半:多线程同一毫秒挤着发号,唯一性由锁 + 毫秒内序列 + 打满自旋保证。
        // 另一半(同机多进程同号)归 WorkerIdLease,见 WorkerIdLeaseTests / WorkerIdGuardTests。
        var gen = new SnowflakeIdGenerator(workerId: 1);
        const int threads = 8, perThread = 4_000;
        var results = new long[threads][];

        Parallel.For(0, threads, t =>
        {
            var mine = new long[perThread];
            for (var i = 0; i < perThread; i++) mine[i] = gen.NextId();
            results[t] = mine;
        });

        var all = results.SelectMany(r => r).ToArray();
        Assert.Equal(threads * perThread, all.Distinct().Count());
        Assert.All(all, id => Assert.Equal(1L, (id >> 6) & 63));   // 机器位始终是构造时给的号
    }
}
