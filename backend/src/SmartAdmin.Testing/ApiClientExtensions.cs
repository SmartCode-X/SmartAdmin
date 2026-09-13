using System.Text;
using System.Text.Json;

namespace SmartAdmin.Testing;

/// <summary>集成测试的 HTTP 小助手:发 JSON、读统一信封、登录取 token。</summary>
public static class ApiClientExtensions
{
    /// <summary>以 JSON 发起 POST(自动序列化 <paramref name="body"/>,Content-Type 为 application/json)。</summary>
    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object body) =>
        client.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    /// <summary>以 JSON 发起 PUT(自动序列化 <paramref name="body"/>,Content-Type 为 application/json)。</summary>
    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object body) =>
        client.PutAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    /// <summary>读响应体为 JSON 根元素(统一信封 { code, msgKey, args, message, data })。</summary>
    public static async Task<JsonElement> ReadEnvelope(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            // 把状态码与正文前缀带上:PUT 返回 "Not Found" / 驱动的纯文本错误时,只看 JSON 解析错看不出到底是 404 还是未处理异常
            var preview = body.Length <= 500 ? body : body[..500] + "…";
            throw new InvalidOperationException(
                $"Expected JSON envelope but got {(int)response.StatusCode} {response.ReasonPhrase}: {preview}",
                ex);
        }
    }

    /// <summary>账密登录,返回 accessToken(失败时抛出信封解析错误,信息里带状态码与正文)。</summary>
    public static async Task<string> LoginToken(this HttpClient client, string account, string password)
    {
        var j = await (await client.PostJson("/api/v1/auth/login", new { account, password })).ReadEnvelope();
        return j.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
