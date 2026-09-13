using Microsoft.AspNetCore.Mvc;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 服务器监控端点——进程与主机基础运行指标。<c>[RolePermission]</c> 授权,
/// 经 <c>Api:DisabledModules=["Monitor"]</c> 可整体关闭。
/// </summary>
[ApiController]
[Route("api/v1/sys/monitor")]
[Module("Monitor")]
public class MonitorController(IMonitorService monitor) : ControllerBase
{
    /// <summary>取服务器运行快照(CPU/内存/磁盘/运行时;含一次约 500ms 的 CPU 采样)</summary>
    [HttpGet("server")]
    [RolePermission]
    public async Task<Result<ServerInfoOutput>> Server(CancellationToken cancellationToken) =>
        Result<ServerInfoOutput>.Ok(await monitor.GetServerInfoAsync(cancellationToken));
}
