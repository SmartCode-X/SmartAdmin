using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 业务异常 → 统一返回的转换过滤器。
/// <para><see cref="AdminException"/>:可预期的业务失败 → HTTP 200 + 业务码信封
/// <c>{ code, msgKey, args, message }</c>,记 Information 级日志(不是错误,不打扰告警)。
/// 其他异常不在这里拦——让框架默认 500 流程处理并留完整堆栈(程序缺陷该大声失败)。</para>
/// </summary>
internal sealed class AdminExceptionFilter(ILogger<AdminExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not AdminException ex) return;

        // 带 innerException 的是"包了一层的外部调用失败":信封不带原始异常,这里不记就彻底丢了,
        // 且它多半是环境故障(磁盘满、鉴权过期),值得比普通业务失败响一格。
        if (ex.InnerException is null)
        {
            logger.LogInformation("业务失败 {Code}({MsgKey}):{Path}",
                (int)ex.Code, ex.MsgKey, context.HttpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(ex.InnerException, "业务失败 {Code}({MsgKey}):{Path}",
                (int)ex.Code, ex.MsgKey, context.HttpContext.Request.Path);
        }

        context.Result = new ObjectResult(Result<object>.From(ex));
        context.ExceptionHandled = true;
    }
}
