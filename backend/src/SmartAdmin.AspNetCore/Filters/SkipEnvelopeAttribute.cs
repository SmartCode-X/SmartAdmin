namespace SmartAdmin.AspNetCore;

/// <summary>
/// 本端点的成功返回<b>不</b>包统一信封:控制器返回什么,响应体就是什么。给对接第三方 / 设备端这类
/// 要求固定响应形状的接口用,不必绕过 <c>ResultEnvelopeFilter</c> 自己拼。OpenAPI 契约同步不加信封外壳。
/// <para>只影响成功路径:业务异常(<c>AdminException</c>)仍走统一信封(200 + code),401 / 403 / 429 也不变——
/// 错误形状要自定义,在动作里 catch 后自己 return。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipEnvelopeAttribute : Attribute;
