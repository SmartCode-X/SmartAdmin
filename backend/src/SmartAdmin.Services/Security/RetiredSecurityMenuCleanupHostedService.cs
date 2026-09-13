using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// 停用指向内核不提供的端点(<see cref="RetiredPermissions"/>:MFA 邀请/重置、安全基线诊断)的菜单权限锚点。
/// 种子 SyncOnUpgrade 只更新仍在种子表中的 Id,不删种子表之外的行;本服务在启动时幂等关掉这些行。
/// </summary>
internal sealed class RetiredSecurityMenuCleanupHostedService(
    IServiceScopeFactory scopes,
    ILogger<RetiredSecurityMenuCleanupHostedService> logger) : IHostedService
{
    private static readonly string[] RetiredPermissions =
    [
        "POST:/api/v1/sys/mfa/invite",
        "DELETE:/api/v1/sys/mfa/invite/{id:long}",
        "POST:/api/v1/sys/mfa/reset",
        "GET:/api/v1/sys/security/baseline",
        "GET:/api/v1/sys/level3/precheck",
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var menus = scope.ServiceProvider.GetRequiredService<IRepository<SysMenu>>();
            // 拷到局部:SqlSugar 无法翻译 private static 字段 Contains(Field "RetiredPermissions" can't be private)
            var retired = RetiredPermissions.ToArray();
            // 含软删过滤外的物理行:清 Enabled 即可,角色上残留的权限码在 UI 上不生效(鉴权仍按码,但端点不存在 → 404)
            var n = await menus.Db.Updateable<SysMenu>()
                .SetColumns(m => new SysMenu { Enabled = false, Visible = false })
                .Where(m => retired.Contains(m.Permission) && (m.Enabled || m.Visible))
                .ExecuteCommandAsync();
            if (n > 0)
                logger.LogInformation(
                    "SmartAdmin: 已禁用 {Count} 行指向已拆除端点的菜单权限锚点。",
                    n);
        }
        catch (Exception ex)
        {
            // 启动不因清理失败而中断
            logger.LogWarning(ex, "SmartAdmin: 退役菜单权限锚点清理已跳过。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
