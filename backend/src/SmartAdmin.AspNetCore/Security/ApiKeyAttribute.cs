using Microsoft.AspNetCore.Authorization;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 机器端接入标记:本端点只认 API Key(请求头 <c>X-Api-Key</c>,可配),不认用户 JWT。
/// 无 key / 错 key → 401 + <see cref="Core.ErrorCode.ApiKeyInvalid"/>;对 key → 放行。
/// <para>只挂本特性 = 认证即放行,不查角色权限。要走 RBAC,把 key 绑到一个用户
/// (<see cref="Core.ApiKeyEntry.UserId"/>)并叠挂 <c>[RolePermission]</c>:权限码、数据范围、操作日志与该用户完全一致。
/// 不要叠 <c>[ActiveSession]</c> 之外还指望"会话"——机器调用没有会话,那两个特性对 key 主体跳过会话校验。</para>
/// <para>同一端点想同时接受 JWT 与 key,直接写 <c>[Authorize(AuthenticationSchemes = "Bearer,ApiKey")]</c>。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class ApiKeyAttribute : AuthorizeAttribute
{
    /// <summary>绑定认证 scheme 为 <see cref="ApiKeyDefaults.Scheme"/>。</summary>
    public ApiKeyAttribute() => AuthenticationSchemes = ApiKeyDefaults.Scheme;
}
