using Microsoft.AspNetCore.Mvc;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 认证连通性探针:最小的受保护接口——带有效令牌返回当前用户,否则 401/403。
/// 前端登录后自检、运维排查令牌问题都用得上;也是 [RolePermission] 管道的常驻冒烟点。
/// </summary>
[ApiController]
[Route("api/v1")]
public class PingController : ControllerBase
{
    /// <summary>返回 pong、当前登录账号与服务器时间;能调通即说明令牌与权限管道工作正常。</summary>
    [HttpGet("ping")]
    [RolePermission]
    public Result<object> Ping() => Result<object>.Ok(new
    {
        pong = true,
        account = User.Identity?.Name,
        at = DateTimeOffset.Now,
    });
}
