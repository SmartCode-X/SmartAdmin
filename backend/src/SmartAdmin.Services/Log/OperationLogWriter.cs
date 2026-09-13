using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 操作日志的异步落库。请求线程只把<b>已经填好上下文的行</b>丢进队列就返回,批量插入在后台跑。
/// <para>请求线程不必等这次数据库往返——审计不该让业务请求收这笔过路费,落库因此放到后台批量执行。</para>
/// <para>队列有界,满了丢最旧的:审计里新的比旧的重要,而阻塞请求线程去等一条日志是更坏的选择。
/// 逼近上限会打告警(节流到每分钟一条),不把丢行咽下去。要绝对不丢就配 <c>Logging:OpLog:Sync=true</c>。</para>
/// </summary>
public sealed class OperationLogWriter : BackgroundService
{
    /// <summary>一批最多插多少行。</summary>
    private const int BATCH = 200;

    /// <summary>停机时留给排空的预算:再多就该让进程走了。</summary>
    private static readonly TimeSpan DRAIN_BUDGET = TimeSpan.FromSeconds(5);

    private readonly Channel<SysOpLog> _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OperationLogWriter> _logger;
    private readonly TimeProvider _time;
    private readonly int _warnDepth;
    private DateTimeOffset _lastWarn = DateTimeOffset.MinValue;

    /// <summary>按日志配置的队列容量建有界队列,持有 DI 范围工厂、日志记录器与时间源供后台落库使用。</summary>
    public OperationLogWriter(
        AdminLoggingOptions options,
        IServiceScopeFactory scopes,
        ILogger<OperationLogWriter> logger,
        TimeProvider time)
    {
        var capacity = options.OpLog.QueueCapacity > 0 ? options.OpLog.QueueCapacity : 10000;
        _queue = Channel.CreateBounded<SysOpLog>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _scopes = scopes;
        _logger = logger;
        _time = time;
        _warnDepth = Math.Max(1, capacity * 9 / 10);
    }

    /// <summary>入队。行里的上下文字段必须<b>调用方已经填好</b>——后台线程上没有 HttpContext。</summary>
    public void Enqueue(SysOpLog row)
    {
        _queue.Writer.TryWrite(row);   // DropOldest:不会失败,满了是最旧的那条被挤掉
        WarnIfBacklogged();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SysOpLog>(BATCH);
        while (await _queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            batch.Clear();
            while (batch.Count < BATCH && _queue.Reader.TryRead(out var row)) batch.Add(row);
            if (batch.Count > 0) await FlushAsync(batch).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // 排空剩余:停机时丢掉手上这几百行审计说不过去,但也不能无限期拖着进程不退
        var deadline = _time.GetUtcNow() + DRAIN_BUDGET;
        var batch = new List<SysOpLog>(BATCH);
        while (_time.GetUtcNow() < deadline)
        {
            batch.Clear();
            while (batch.Count < BATCH && _queue.Reader.TryRead(out var row)) batch.Add(row);
            if (batch.Count == 0) break;
            await FlushAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(List<SysOpLog> batch)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysOpLog>>();
            await repo.Db.Insertable(batch).ExecuteCommandAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 尽力而为:日志写不进去不能把后台服务带崩,那会连带停掉后续所有审计
            _logger.LogWarning(ex, "操作日志批量写入失败,丢弃 {Count} 行。", batch.Count);
        }
    }

    /// <summary>积压逼近上限时告警,节流到每分钟一条——告警本身别成为第二场洪水。</summary>
    private void WarnIfBacklogged()
    {
        if (_queue.Reader.Count < _warnDepth) return;
        var now = _time.GetUtcNow();
        if (now - _lastWarn < TimeSpan.FromMinutes(1)) return;
        _lastWarn = now;
        _logger.LogWarning(
            "操作日志队列积压 {Depth} 条(容量上限附近),最旧的行正在被丢弃。" +
            "调大 Logging:OpLog:QueueCapacity,或确认数据库写入是否变慢。",
            _queue.Reader.Count);
    }
}
