namespace SmartAdmin.SqlSugar;

/// <summary>
/// 一次 HTTP 请求 / 一次任务触发内的 SQL 统计。用 <see cref="AsyncLocal{T}"/> 携带,所以
/// AOP 钩子(跑在业务线程上,拿不到 HttpContext)也能把语句记到当前这次调用名下。
/// <para>这里可以用 AsyncLocal,而数据范围载体不行:数据范围要从鉴权过滤器往回流,AsyncLocal 流不回去;
/// 统计只往下流。</para>
/// </summary>
public static class SqlLogScope
{
    private static readonly AsyncLocal<SqlLogStats?> CURRENT = new();

    /// <summary>当前统计;不在任何 scope 内(启动期建表、后台线程)时为 null。</summary>
    public static SqlLogStats? Current => CURRENT.Value;

    /// <summary>开一个统计范围。<paramref name="label"/> 是汇总行里的标识(如 <c>GET /api/v1/sys/user/page</c>)。</summary>
    public static IDisposable Begin(string label)
    {
        var previous = CURRENT.Value;
        CURRENT.Value = new SqlLogStats(label);
        return new Restore(previous);
    }

    private sealed class Restore(SqlLogStats? previous) : IDisposable
    {
        public void Dispose() => CURRENT.Value = previous;
    }
}

/// <summary>一个统计范围内的累计值。跨线程累加,故加锁——SQL 往返本身比这把锁贵几个数量级。</summary>
public sealed class SqlLogStats(string label)
{
    private readonly Lock _gate = new();

    /// <summary>范围标识(请求方法+路径 / 任务编码)。</summary>
    public string Label { get; } = label;

    /// <summary>已执行语句数。</summary>
    public int Count { get; private set; }

    /// <summary>合计耗时(毫秒)。</summary>
    public double TotalMillis { get; private set; }

    /// <summary>最慢一条的耗时(毫秒)。</summary>
    public double SlowestMillis { get; private set; }

    /// <summary>最慢一条的序号(与语句块头部的 <c>#n</c> 对得上)。</summary>
    public int SlowestIndex { get; private set; }

    /// <summary>记一条,返回它在本范围内的序号。</summary>
    public int Record(double millis)
    {
        lock (_gate)
        {
            Count++;
            TotalMillis += millis;
            if (millis > SlowestMillis)
            {
                SlowestMillis = millis;
                SlowestIndex = Count;
            }
            return Count;
        }
    }
}
