using Microsoft.Extensions.Logging;
using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IEmailSender"/> 默认实现:只把邮件写进日志(Information)。
/// <para>这是<b>开发/演示通道</b>——本地跑通全流程不需要任何邮件账号,内容在后端控制台可见。
/// 生产接真实邮件:配 <c>SmartAdmin:Email:Host</c> 用内置 SMTP,或实现 <see cref="IEmailSender"/>
/// 并在 <c>AddSmartAdmin()</c> 前注册即接管。</para>
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[EMAIL] → {To} | {Subject}(日志通道,生产请配 SMTP 或注册真实 IEmailSender)",
            to, subject);
        return Task.CompletedTask;
    }
}
