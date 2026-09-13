using SmartAdmin.Core;

namespace SmartAdmin.TestHost;

/// <summary>
/// 消费者自有错误码——照真实消费方的写法:另占一个码段,成员带 <c>[MsgKey]</c>,
/// 抛出时强转成内核的 <see cref="ErrorCode"/>。本程序集已登记为 ApplicationAssemblies,
/// 装配时会被自动扫进错误码目录,信封里的 msgKey 应当解析到这里的键而不是回退成 error.code.60001。
/// </summary>
public enum SampleErrorCode
{
    /// <summary>示例:部件正忙</summary>
    [MsgKey("error.sample.widgetBusy")]
    WidgetBusy = 60001,
}
