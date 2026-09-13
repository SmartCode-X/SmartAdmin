using Microsoft.AspNetCore.Mvc;
using SmartAdmin.Core;
using SmartAdmin.Services;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 全局回收站——按实体类型查看软删除数据、恢复或彻底删除。
/// 路由 <c>/api/v1/sys/recycle/{type}/...</c>;type 取值由登记决定:内核九种,消费者用
/// <c>AddRecycleBinType&lt;T&gt;()</c> 把自己的软删表接进来。
/// </summary>
[ApiController]
[Route("api/v1/sys/recycle")]
[Module("RecycleBin")]
public class RecycleBinController(IRecycleBinService recycle) : ControllerBase
{
    /// <summary>分页列出指定类型的已删记录</summary>
    [HttpGet("{type}/page")]
    [RolePermission]
    public async Task<Result<PagedList<RecycleBinItem>>> Page(string type, [FromQuery] RecycleBinPageInput input) =>
        Result<PagedList<RecycleBinItem>>.Ok(await recycle.PageAsync(type, input));

    /// <summary>恢复已删记录</summary>
    [HttpPost("{type}/{id}/restore")]
    [RolePermission]
    [OperationLog("回收站-恢复")]
    public async Task<Result<bool>> Restore(string type, long id)
    {
        await recycle.RestoreAsync(type, id);
        return Result<bool>.Ok(true);
    }

    /// <summary>彻底删除(物理删除,不可恢复)</summary>
    [HttpDelete("{type}/{id}")]
    [RolePermission]
    [RequireReauth]                     // 不可逆硬删,启用 TOTP 时要求短时再认证
    [OperationLog("回收站-彻底删除")]   // 不可逆硬删,必须留审计
    public async Task<Result<bool>> Purge(string type, long id)
    {
        await recycle.PurgeAsync(type, id);
        return Result<bool>.Ok(true);
    }
}
