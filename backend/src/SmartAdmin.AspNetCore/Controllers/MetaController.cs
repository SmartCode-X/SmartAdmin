using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 契约元数据。目前只有错误码目录。
/// <para><b>匿名</b>:它就是一张公开的码表(数值 + 枚举成员名 + i18n 键),不含任何业务数据,
/// 而需要它的场合——登录页翻译错误、外部集成方对接、按契约调用的 agent——多半还没有令牌。</para>
/// <para>没有做 <c>meta/permissions</c>:权限码清单在 <c>menu/routes</c> 已有,那条是<b>挂权限的</b>,
/// 再开一个匿名别名等于把"这个系统有哪些端点"白送给侦察。</para>
/// </summary>
[ApiController]
[Route("api/v1/meta")]
[Module("Meta")]   // 可经 Api:DisabledModules=["Meta"] 关闭
public class MetaController(IErrorCodeCatalog catalog) : ControllerBase
{
    /// <summary>
    /// 错误码目录:数值、枚举成员名、语义 msgKey,含消费者登记进来的码。
    /// <para>信封里的 <c>code</c> 是个数字,前端按 <c>msgKey</c> 查文案;若这张对照表只存在于代码里,
    /// 外部集成方拿不到,只能照着文档手抄。</para>
    /// </summary>
    [HttpGet("error-codes")]
    [AllowAnonymous]
    public Result<IReadOnlyList<ErrorCodeItem>> ErrorCodes() =>
        Result<IReadOnlyList<ErrorCodeItem>>.Ok(
            [.. catalog.All.Select(c => new ErrorCodeItem
            {
                Code = c.Code,
                Name = c.Name,
                MsgKey = c.MsgKey,
                Source = c.EnumType.Name,
            })]);
}

/// <summary>错误码目录的一项。</summary>
public class ErrorCodeItem
{
    /// <summary>数值(统一信封里的 <c>code</c>)</summary>
    public int Code { get; set; }

    /// <summary>枚举成员名,如 <c>NoPermission</c></summary>
    public string Name { get; set; } = "";

    /// <summary>前端 i18n 键,如 <c>error.auth.noPermission</c></summary>
    public string MsgKey { get; set; } = "";

    /// <summary>来源枚举类型名(内核的是 <c>ErrorCode</c>,消费者的是自己那个)</summary>
    public string Source { get; set; } = "";
}
