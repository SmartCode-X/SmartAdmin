using System.Text.Json;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// OpenAPI 契约里那些"机器要读"的部分。
/// <para>不加这层,这份文档对人尚可、对机器很贫瘠:同名操作(如 <c>Delete</c>/<c>Update</c>,跨多个资源各来一份)全靠路径区分;
/// "这个端点要什么权限"明明是可计算的(权限码就是规范化路由),契约里却一个字都没有;
/// 业务错误全走 200 + 信封里的 <c>code</c>,而 <c>code</c> 在契约里只是个 integer。</para>
/// </summary>
public class OpenApiContractTests
{
    private static async Task<JsonElement> DocumentAsync(AdminAppFactory f)
    {
        var json = await f.CreateClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Operations(JsonElement doc) =>
        OperationsWithRoute(doc).Select(x => x.Operation);

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> OperationsWithRoute(JsonElement doc)
    {
        foreach (var path in doc.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
                if (op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out _))
                    yield return (path.Name, op.Name, op.Value);
    }

    [Fact]
    public async Task 每个操作都有唯一的operationId()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var ids = Operations(doc).Select(o => o.GetProperty("operationId").GetString()!).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[A-Za-z][A-Za-z0-9_]*$", id));
    }

    /// <summary>operationId 要稳:它会变成生成客户端里的方法名,漂一次就是一次无声的破坏性变更。</summary>
    [Fact]
    public async Task operationId是控制器加动作名()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var ids = Operations(doc).Select(o => o.GetProperty("operationId").GetString()!).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Meta_ErrorCodes", ids);
        Assert.Contains(ids, id => id.StartsWith("User_", StringComparison.Ordinal));
    }

    /// <summary>
    /// 契约里的权限码必须与"按这条路由算出来的"完全一致——授权判定走的就是 PermissionCode.Build,
    /// 两边差一个字符就是"授了也匹配不上",而且没有任何编译或运行期报错。
    /// </summary>
    [Fact]
    public async Task 权限码与路由自洽()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var checkedCount = 0;
        foreach (var (path, method, op) in OperationsWithRoute(doc))
        {
            if (!op.TryGetProperty("x-permission-code", out var c)) continue;
            Assert.Equal(PermissionCode.Build(method, path), c.GetString());
            checkedCount++;
        }

        Assert.True(checkedCount > 50, $"只有 {checkedCount} 个端点带出了权限码,内置端点远不止这些");
    }

    [Fact]
    public async Task 三类鉴权形态都标了出来()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var auths = Operations(doc)
            .Where(o => o.TryGetProperty("x-auth", out _))
            .Select(o => o.GetProperty("x-auth").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("anonymous", auths);     // 登录、验证码、错误码目录
        Assert.Contains("permission", auths);    // 绝大多数管理端点
        Assert.Contains("session", auths);       // 任何登录用户可读的那些
    }

    /// <summary>
    /// 裸返回 dto 的端点,契约里的 200 必须也是信封——运行时本来就会被包成信封,
    /// 契约不跟上,照它生成的前端类型就会把 data 当成顶层字段,而且没有任何报错。
    /// </summary>
    [Fact]
    public async Task 裸返回端点的契约也是信封()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var schema = doc.GetProperty("paths").GetProperty("/api/v1/diag/bare-dto")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        var props = schema.GetProperty("properties");
        Assert.True(props.TryGetProperty("code", out _));
        Assert.True(props.TryGetProperty("msgKey", out _));
        // data 里才是原始那个 dto
        Assert.True(props.GetProperty("data").TryGetProperty("$ref", out _)
                    || props.GetProperty("data").TryGetProperty("properties", out _));
    }

    /// <summary>契约说是信封,运行时就得真是信封——这条把两边钉在一起。</summary>
    [Fact]
    public async Task 裸返回端点运行时与契约一致()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var body = await (await c.GetAsync("/api/v1/diag/bare-dto")).ReadEnvelope();

        Assert.Equal(0, body.GetProperty("code").GetInt32());
        Assert.Equal("裸返回", body.GetProperty("data").GetProperty("name").GetString());
    }

    /// <summary>显式返回 Result&lt;T&gt; 的端点不能被包第二层。</summary>
    [Fact]
    public async Task 已是信封的端点不重复包裹()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var schema = doc.GetProperty("paths").GetProperty("/api/v1/diag/page-echo")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        // 直接引用 ResultOfInt32 之类的既有 schema,而不是一个又套了一层的匿名对象
        Assert.True(schema.TryGetProperty("$ref", out _));
    }

    [Fact]
    public async Task 错误码目录进了契约()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };
        var doc = await DocumentAsync(f);

        var schema = doc.GetProperty("components").GetProperty("schemas").GetProperty("ErrorCode");
        // 刻意不写 enum:带 enum 的整数会被生成器渲染成字面量联合,而码空间对消费者是开放的
        Assert.False(schema.TryGetProperty("enum", out _));
        var values = schema.GetProperty("x-enum-values").EnumerateArray().Select(v => v.GetInt32()).ToList();
        var names = schema.GetProperty("x-enum-varnames").EnumerateArray().Select(v => v.GetString()).ToList();
        var keys = schema.GetProperty("x-msg-keys").EnumerateArray().Select(v => v.GetString()).ToList();

        Assert.Equal(values.Count, names.Count);
        Assert.Equal(values.Count, keys.Count);
        Assert.Contains((int)ErrorCode.NoPermission, values);
        Assert.Contains("NoPermission", names);
        // 0 号是 Success,它的键是 common.success;其余一律 error.*
        Assert.Equal("common.success", keys[values.IndexOf(0)]);
        Assert.All(keys.Where((_, i) => values[i] != 0), k => Assert.StartsWith("error.", k!, StringComparison.Ordinal));
    }
}

/// <summary>
/// 错误码目录端点。信封里的 <c>code</c> 是个数字,前端按 <c>msgKey</c> 查文案;
/// 若这张对照表只存在于代码里,外部集成方只能照着文档手抄。
/// </summary>
public class MetaEndpointTests
{
    [Fact]
    public async Task 匿名可取错误码目录()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };

        var data = (await (await f.CreateClient().GetAsync("/api/v1/meta/error-codes")).ReadEnvelope())
            .GetProperty("data");

        var items = data.EnumerateArray().ToList();
        Assert.NotEmpty(items);

        var noPermission = items.Single(i => i.GetProperty("code").GetInt32() == (int)ErrorCode.NoPermission);
        Assert.Equal("NoPermission", noPermission.GetProperty("name").GetString());
        Assert.Equal("ErrorCode", noPermission.GetProperty("source").GetString());
        Assert.StartsWith("error.", noPermission.GetProperty("msgKey").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>消费者登记进来的码也要出现:不出现的话它们在前端只能显示成 error.code.60001。</summary>
    [Fact]
    public async Task 目录含消费者登记的码()
    {
        using var f = new AdminAppFactory { DisabledModules = [] };

        var data = (await (await f.CreateClient().GetAsync("/api/v1/meta/error-codes")).ReadEnvelope())
            .GetProperty("data");

        var sources = data.EnumerateArray()
            .Select(i => i.GetProperty("source").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("SampleErrorCode", sources);   // TestHost 是个真实消费者宿主
    }

    [Fact]
    public async Task 模块可整体关掉()
    {
        using var f = new AdminAppFactory { DisabledModules = ["Meta"] };

        var resp = await f.CreateClient().GetAsync("/api/v1/meta/error-codes");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }
}
