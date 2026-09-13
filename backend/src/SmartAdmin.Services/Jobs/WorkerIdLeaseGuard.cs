using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;
using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// 本进程的节点身份:机器名、进程 Id,以及"某个 pid 还活着吗"的判定。
/// <para>抽成一块可注入的东西,是因为守卫最关键的判断——"库里那行租约是别人的,还是我上一世留下的"——全靠它,
/// 而测试开不出第二个进程。</para>
/// </summary>
public sealed record WorkerLeaseIdentity(string MachineName, int Pid, Func<int, bool> IsProcessAlive)
{
    /// <summary>当前进程的真实身份。</summary>
    public static WorkerLeaseIdentity Current { get; } = new(Environment.MachineName, Environment.ProcessId, IsAlive);

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }         // 进程不存在
        catch (InvalidOperationException) { return false; } // 查询期间退出
        catch { return true; }                              // 权限不足等:拿不准就当它活着,宁可拒绝启动也不发重复号
    }
}

/// <summary>
/// 雪花 WorkerId 数据库租约守卫:启动时争抢租约,周期续租,停止时释放。
/// 同一 WorkerId 不能被两个<b>活着的</b>实例同时持有——真冲突时在 <see cref="StartAsync"/> 抛异常,
/// 把静默的主键冲突换成一条可读的启动错误。
/// <para><b>什么算真冲突</b>:节点名分两截,<c>@</c> 前是稳定身份 <c>{机器名}#{机器号}</c>,<c>@</c> 后是每次启动都变的实例 token
/// (续租与释放拿完整节点名做条件写,免得旧进程回魂续了新主的租约)。稳定身份相同 = 同机同号,那行租约要么是被硬杀的上一世
/// (pid 已死),要么是容器重启后 pid 又落回同一个号(pid 就是我自己),两种都直接接管。只有"同机另一个还活着的进程"
/// 和"另一台机器"才拒绝启动。</para>
/// <para>不这么分,判定就只剩"节点名不等于我"——而节点名里带随机 token,进程重启后<b>连自己都认不出来</b>,
/// 于是每次非正常退出(停止调试、关控制台窗口、任务管理器)都要干等一个 TTL 才能再起,开发期尤其难受。</para>
/// <para>租约 TTL = <see cref="AdminJobsOptions.HeartbeatSeconds"/> × 3;续租间隔 = HeartbeatSeconds / 2。
/// TTL 只在跨机器场景兜底(pid 在别的机器上没有意义),同机不靠等 TTL。</para>
/// </summary>
public sealed class WorkerIdLeaseGuard(
    ISqlSugarClient db,
    AdminIdOptions idOptions,
    AdminJobsOptions jobsOptions,
    ILogger<WorkerIdLeaseGuard> logger,
    TimeProvider? time = null,
    WorkerLeaseIdentity? identity = null) : IHostedService, IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly WorkerLeaseIdentity _identity = identity ?? WorkerLeaseIdentity.Current;
    private readonly string _instanceToken = Guid.NewGuid().ToString("N")[..8];
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private int _workerId;
    private string _stableName = "";
    private string _nodeName = "";

    private DateTime Now => _time.GetLocalNow().DateTime;
    private int HeartbeatSeconds => Math.Max(jobsOptions.HeartbeatSeconds, 2);
    private TimeSpan LeaseTtl => TimeSpan.FromSeconds(HeartbeatSeconds * 3);
    private TimeSpan RenewInterval => TimeSpan.FromSeconds(HeartbeatSeconds / 2.0);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _workerId = idOptions.WorkerId ?? 0;
        _stableName = $"{_identity.MachineName}#{_workerId}";
        _nodeName = $"{_stableName}@{_instanceToken}";

        db.CodeFirst.InitTables<SysWorkerLease>();
        await AcquireAsync();

        logger.LogInformation("WorkerId {WorkerId} 租约已获取(node={Node}, pid={Pid}, ttl={Ttl}s)。",
            _workerId, _nodeName, _identity.Pid, LeaseTtl.TotalSeconds);

        // 自有 CTS,不要 CreateLinkedTokenSource(StartAsync 的 token):
        // 宿主停机时会先 Dispose 启动链上的 token source,再调 StopAsync——
        // 链到它的 CTS 已被 Dispose,CancelAsync 会抛 ObjectDisposedException,拖垮整批 WebApplicationFactory 测试。
        _cts = new CancellationTokenSource();
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
    }

    /// <summary>
    /// 抢租约:读 → 判 → 条件写。条件写是必须的——两个实例同时开机会读到同一行"前任已死"并同时决定接管,
    /// 只有影响行数分得出胜负;插入撞唯一索引同理,回头按已有行重判。
    /// </summary>
    private async Task AcquireAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            var now = Now;
            var expiresAt = now + LeaseTtl;
            var existing = await db.Queryable<SysWorkerLease>()
                .Where(l => l.WorkerId == _workerId)
                .FirstAsync();

            if (existing is null)
            {
                try
                {
                    await db.Insertable(new SysWorkerLease
                    {
                        WorkerId = _workerId,
                        NodeName = _nodeName,
                        Pid = _identity.Pid,
                        LeaseExpiresAt = expiresAt,
                    }).ExecuteCommandAsync();
                    return;
                }
                catch when (attempt < 2)
                {
                    continue;   // 唯一索引撞了:别人先插进去,回头按已有行重新判
                }
            }
            else
            {
                var held = existing.LeaseExpiresAt > now;
                if (held && !CanTakeOver(existing))
                    throw Conflict(existing, now);

                var previous = existing.NodeName;
                var nodeName = _nodeName;
                var pid = _identity.Pid;
                var rows = await db.Updateable<SysWorkerLease>()
                    .SetColumns(l => new SysWorkerLease { NodeName = nodeName, Pid = pid, LeaseExpiresAt = expiresAt })
                    .Where(l => l.WorkerId == _workerId && l.NodeName == previous)
                    .ExecuteCommandAsync();

                if (rows > 0)
                {
                    if (held)
                        logger.LogWarning("WorkerId {WorkerId} 接管了残留租约(前节点 {Previous}, pid={Pid} 已不在)。",
                            _workerId, previous, existing.Pid);
                    return;
                }
                if (attempt < 2) continue;   // 这一瞬被别的实例改走了,重读再判
            }

            throw new InvalidOperationException(
                $"WorkerId {_workerId} 租约连续争抢失败,可能有多个实例正在同时启动并使用同一个机器号。" +
                "请为每个实例配置不同的 SmartAdmin:Id:WorkerId(0–63)。");
        }
    }

    /// <summary>能不能接管这行还没到期的租约:同机同号、且前任已经不在,就能。</summary>
    private bool CanTakeOver(SysWorkerLease existing)
    {
        if (StableNameOf(existing.NodeName) != _stableName) return false;   // 另一台机器,pid 在这边没有意义
        if (existing.Pid == _identity.Pid) return true;                     // 上一世的自己(容器重启后 pid 复用)
        return !_identity.IsProcessAlive(existing.Pid);
    }

    /// <summary>取节点名里 <c>@</c> 之前的稳定身份 <c>{机器名}#{机器号}</c>。</summary>
    private static string StableNameOf(string nodeName)
    {
        var at = nodeName.IndexOf('@');
        return at < 0 ? nodeName : nodeName[..at];
    }

    private InvalidOperationException Conflict(SysWorkerLease existing, DateTime now)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling((existing.LeaseExpiresAt - now).TotalSeconds));
        var sameMachine = StableNameOf(existing.NodeName) == _stableName;
        return new InvalidOperationException(
            $"WorkerId {_workerId} 正被节点 \"{existing.NodeName}\"(pid={existing.Pid})持有,租约 {seconds} 秒后到期。" +
            (sameMachine
                ? "本机另一个进程正用着同一个机器号,且该进程仍在运行。"
                : "该节点在另一台机器上,而两个实例连的是同一个库——同号会在同一毫秒发出重复 Id。") +
            "请为本实例配置不同的 SmartAdmin:Id:WorkerId(0–63);按机器区分建议用环境变量 SMARTADMIN__Id__WorkerId,别写进随代码分发的配置文件。" +
            $"若确认对方已经下线,可删掉残留租约:DELETE FROM sys_worker_lease WHERE worker_id = {_workerId}。");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            try { await cts.CancelAsync(); }
            catch (ObjectDisposedException) { /* already torn down */ }

            if (_heartbeatTask is not null)
            {
                // 循环内部已吞掉所有续租异常,唯一还能逃出来的是它那句告警日志本身
                // (停机期日志提供者可能已释放,同 StopAsync 尾部)。停机不该被它拖垮。
                try { await _heartbeatTask; }
                catch { /* 取消或日志提供者已释放 */ }
            }

            cts.Dispose();
        }

        try
        {
            await db.Deleteable<SysWorkerLease>()
                .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                .ExecuteCommandAsync();
            logger.LogInformation("WorkerId {WorkerId} 租约已释放。", _workerId);
        }
        catch (Exception ex)
        {
            // 释放是尽力而为:进程被硬杀时根本走不到这儿,靠的是下次启动的同机接管。停机期日志提供者可能已被释放
            // (Windows EventLog 就会抛 ObjectDisposedException),所以告警本身也要兜住——任何异常冒出 StopAsync,
            // Host.StopAsync 都会聚合抛出,连带拖垮正在销毁的 WebApplicationFactory。
            try
            {
                logger.LogWarning(ex, "WorkerId {WorkerId} 租约释放失败(进程退出后由下次启动接管或自然过期)。", _workerId);
            }
            catch { /* 日志提供者已随宿主停机释放,无处可报 */ }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RenewInterval, _time, ct);
                var expiresAt = Now + LeaseTtl;
                await db.Updateable<SysWorkerLease>()
                    .SetColumns(l => new SysWorkerLease { LeaseExpiresAt = expiresAt })
                    .Where(l => l.WorkerId == _workerId && l.NodeName == _nodeName)
                    .ExecuteCommandAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WorkerId {WorkerId} 租约续租失败,下轮重试。", _workerId);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Dispose();
    }
}
