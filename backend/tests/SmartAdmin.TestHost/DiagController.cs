using Microsoft.AspNetCore.Mvc;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;

namespace SmartAdmin.TestHost;

/// <summary>
/// 测试专用诊断控制器——故意抛异常,供异常日志过滤器(<c>ExceptionLogFilter</c>)的集成用例验证:
/// 未捕获异常落一条 <c>sys_exception_log</c> 且 500 照旧;业务异常(<c>AdminException</c>)不落表且照返信封。
/// <para><c>[ActiveSession]</c>:任一登录用户可打(无需具体权限码),让异常携带触发人身份以验证操作人回填。</para>
/// </summary>
[ApiController]
[Route("api/v1/diag")]
public class DiagController : ControllerBase
{
    /// <summary>故意抛未捕获异常(程序缺陷)→ 应产生异常日志 + 500。</summary>
    [HttpGet("throw")]
    [ActiveSession]
    public IActionResult Throw() => throw new InvalidOperationException("boom-diag");

    /// <summary>故意抛业务异常 → 应转信封(200 + 业务码),且不进异常日志表。</summary>
    [HttpGet("throw-business")]
    [ActiveSession]
    public IActionResult ThrowBusiness() => throw new AdminException(ErrorCode.PasswordWrong);

    /// <summary>抛消费者自有错误码(强转成内核 ErrorCode)→ 信封 msgKey 应取自消费者枚举上的 [MsgKey]。</summary>
    [HttpGet("throw-consumer-code")]
    [ActiveSession]
    public IActionResult ThrowConsumerCode() => throw new AdminException((ErrorCode)SampleErrorCode.WidgetBusy);

    /// <summary>直接把 PageInputBase 当查询入参:它是具体类(非抽象),模型绑定应能构造出实例。</summary>
    [HttpGet("page-echo")]
    [ActiveSession]
    public Result<int> PageEcho([FromQuery] PageInputBase input) => Result<int>.Ok(input.Size);

    /// <summary>
    /// 消费者常见写法:直接 return dto,由 ResultEnvelopeFilter 兜底包信封。
    /// 契约里这一条的 200 schema 必须也是信封,否则照契约生成的前端类型会把 data 当顶层字段。
    /// </summary>
    [HttpGet("bare-dto")]
    [ActiveSession]
    public BareDto Bare() => new() { Name = "裸返回", Size = 7 };

    // ── 机器端接入([ApiKey]):供 ApiKeyAuthTests 验证四种形态 ──

    /// <summary>只挂 [ApiKey]:对 key 即放行,不查角色;回 key 的名字(unique_name)。</summary>
    [HttpGet("machine")]
    [ApiKey]
    public Result<string> Machine() => Result<string>.Ok(User.Identity?.Name ?? "");

    /// <summary>[ApiKey] + [RolePermission]:key 绑定的用户得持有 GET:/api/v1/diag/machine-perm 权限码。</summary>
    [HttpGet("machine-perm")]
    [ApiKey]
    [RolePermission]
    public Result<string> MachinePerm() => Result<string>.Ok("perm");

    /// <summary>[ApiKey] + [SkipEnvelope]:成功返回不包信封,响应体就是 dto 本身。</summary>
    [HttpGet("machine-raw")]
    [ApiKey]
    [SkipEnvelope]
    public BareDto MachineRaw() => new() { Name = "raw", Size = 1 };

    /// <summary>[ApiKey] 写端点:走操作日志(机器写操作也要留痕)。</summary>
    [HttpPost("machine-write")]
    [ApiKey]
    public Result<bool> MachineWrite() => Result<bool>.Ok(true);
}

/// <summary>裸返回端点的出参(故意不包信封)。</summary>
public class BareDto
{
    public string Name { get; set; } = "";
    public int Size { get; set; }
}
