namespace SmartAdmin.Core;

/// <summary>
/// 敏感字段名关键字——「这个名字看起来像口令/密钥/令牌」的唯一判据。
/// <para>操作日志的入参脱敏与 SQL 诊断日志的参数打码共用同一份:两条出口若各拿一份名单,
/// 补了这边漏了那边,敏感值照样从另一条路落盘。</para>
/// </summary>
public static class SensitiveKeys
{
    /// <summary>
    /// 关键字(小写,子串匹配:<c>newPassword</c>、<c>access_token</c> 都能命中)。
    /// <para>header / authorization / apikey / cookie 是给定时任务的 HTTP 载荷用的——它把请求头整包塞在一个键里,
    /// 不加这几个词,Bearer 令牌会原文落进 <c>sys_op_log.ParamJson</c>。</para>
    /// </summary>
    public static readonly string[] Default =
        ["password", "pwd", "secret", "token", "credential", "header", "authorization", "apikey", "api_key", "cookie"];

    /// <summary>名字是否命中敏感关键字。</summary>
    public static bool IsSensitive(string? name) =>
        !string.IsNullOrEmpty(name) && Default.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
