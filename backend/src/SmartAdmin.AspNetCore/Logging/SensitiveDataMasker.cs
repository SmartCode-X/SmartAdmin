using System.Text.Json;
using System.Text.Json.Nodes;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 入参脱敏——把动作入参序列化成 JSON,并把<b>名字像密码/密钥/令牌</b>的字段值替换为 <c>***</c>,
/// 再写进操作日志。避免明文口令随日志落库。
/// <para>按<b>字段名</b>脱敏(不看值,名单见 <see cref="SensitiveKeys"/>):命中的属性一律打码,
/// 递归处理嵌套对象与数组。序列化失败(如含 IFormFile 等不可序列化入参)不阻断请求,记占位串。</para>
/// </summary>
public static class SensitiveDataMasker
{
    private const string REDACTED = "***";
    private const string UNSERIALIZABLE = "<unserializable>";

    /// <summary>
    /// 把动作入参字典(参数名 → 值)脱敏序列化为 JSON 字符串。
    /// </summary>
    /// <param name="arguments">动作入参</param>
    /// <param name="maxChars">
    /// 字符上限,<c>&lt;= 0</c> 不限。超限时只留开头并标注原长度——<b>不是直接截断</b>:
    /// 从中间切开的 JSON 前端解析不了,而这个字段是要在日志详情里展开看的。
    /// </param>
    public static string Mask(IDictionary<string, object?> arguments, int maxChars = 0)
    {
        try
        {
            // 先整体序列化成可变 JSON 树,再原地打码——比逐类型反射简单且对匿名/嵌套类型通用
            var node = JsonSerializer.SerializeToNode(arguments);
            Redact(node);
            var json = node?.ToJsonString() ?? "null";
            return maxChars > 0 && json.Length > maxChars ? Shorten(json, maxChars) : json;
        }
        catch
        {
            // 不可序列化的入参(文件流、循环引用等)不能拖垮请求;只记占位
            return UNSERIALIZABLE;
        }
    }

    /// <summary>超长入参裹进一个合法的 JSON 壳:留开头一段供辨认,并说明原本多长。</summary>
    private static string Shorten(string json, int maxChars) =>
        new JsonObject
        {
            ["_truncated"] = true,
            ["_originalChars"] = json.Length,
            ["_head"] = json[..Math.Max(0, Math.Min(maxChars, json.Length))],
        }.ToJsonString();

    /// <summary>递归遍历 JSON 树:对象里命中敏感名的属性打码,其余下钻;数组逐元素下钻。</summary>
    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // 先快照键集合:遍历期间要改属性值,不能在原集合上边改边遍历
                foreach (var key in obj.Select(kv => kv.Key).ToArray())
                {
                    if (SensitiveKeys.IsSensitive(key)) obj[key] = REDACTED;
                    else Redact(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Redact(item);
                break;
        }
    }
}
