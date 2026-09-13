using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 操作日志的异步落库与入参上限。
/// <para>若那条 INSERT 挂在响应之前(同步写),每个写请求都要多等它一次数据库往返;而入参 JSON 完全无界,
/// 一次五千行的导入提交,整份数据都会原样躺进日志表。</para>
/// <para>本文件里的宿主显式关掉 <c>Sync</c>——其余用例统一走同步,免得"发请求再断言日志"变成竞态。</para>
/// </summary>
public class OperationLogAsyncTests
{
    private static AdminAppFactory Async(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?> { ["SmartAdmin:Logging:OpLog:Sync"] = "false" };
        foreach (var (k, v) in extra) settings[k] = v;
        // 测试宿主默认禁掉 Dict 模块,这里要拿它当"随便一个写端点"用,得放开
        return new AdminAppFactory { Settings = settings, DisabledModules = [] };
    }

    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>轮询等后台批量落库(它是攒批写的,不是入队即插)。</summary>
    private static async Task<SysOpLog?> WaitForLogAsync(IRepository<SysOpLog> repo, string pathFragment)
    {
        for (var i = 0; i < 50; i++)
        {
            var row = await repo.AsQueryable()
                .Where(l => l.Path.Contains(pathFragment))
                .OrderByDescending(l => l.Id)
                .FirstAsync();
            if (row is not null) return row;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task 异步路径最终把日志落库()
    {
        using var f = Async();
        var c = await SuperAdminClient(f);

        await c.PostJson("/api/v1/sys/dict/type", new
        {
            code = "async-oplog", name = "异步日志", sort = 1, enabled = true,
        });

        using var scope = f.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysOpLog>>();
        var row = await WaitForLogAsync(repo, "/sys/dict/type");

        Assert.NotNull(row);
        Assert.Equal("POST", row!.HttpMethod);
    }

    /// <summary>操作人与发生时刻必须在请求线程上快照——后台线程上已经没有 HttpContext 了。</summary>
    [Fact]
    public async Task 异步落库仍带上操作人()
    {
        using var f = Async();
        var c = await SuperAdminClient(f);

        await c.PostJson("/api/v1/sys/dict/type", new
        {
            code = "async-operator", name = "异步操作人", sort = 1, enabled = true,
        });

        using var scope = f.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SysOpLog>>();
        var row = await WaitForLogAsync(repo, "/sys/dict/type");

        Assert.NotNull(row);
        Assert.NotNull(row!.OperatorId);
        Assert.NotEqual(default, row.CreateTime);
    }

    /// <summary>超长入参只留开头,且结果仍是<b>合法 JSON</b>——从中间切开的话日志详情页解析不了。</summary>
    [Fact]
    public void 超长入参裹成合法JSON并标注原长度()
    {
        var args = new Dictionary<string, object?> { ["payload"] = new string('x', 5000) };

        var json = SmartAdmin.AspNetCore.SensitiveDataMasker.Mask(args, maxChars: 500);

        var node = JsonDocument.Parse(json);   // 解析不了就直接抛,这正是本条要守的东西
        Assert.True(node.RootElement.GetProperty("_truncated").GetBoolean());
        Assert.True(node.RootElement.GetProperty("_originalChars").GetInt32() > 5000);
        Assert.True(node.RootElement.GetProperty("_head").GetString()!.Length <= 500);
    }

    [Fact]
    public void 入参不超限时原样输出()
    {
        var args = new Dictionary<string, object?> { ["name"] = "zhangsan" };

        var json = SmartAdmin.AspNetCore.SensitiveDataMasker.Mask(args, maxChars: 8192);

        Assert.Contains("zhangsan", json, StringComparison.Ordinal);
        Assert.DoesNotContain("_truncated", json, StringComparison.Ordinal);
    }

    /// <summary>上限设 0 = 不限。</summary>
    [Fact]
    public void 上限设零时不截断()
    {
        var args = new Dictionary<string, object?> { ["payload"] = new string('x', 5000) };

        var json = SmartAdmin.AspNetCore.SensitiveDataMasker.Mask(args, maxChars: 0);

        Assert.DoesNotContain("_truncated", json, StringComparison.Ordinal);
        Assert.True(json.Length > 5000);
    }

    /// <summary>截断发生在脱敏之后:敏感字段不能因为被截断而漏出来。</summary>
    [Fact]
    public void 截断不影响脱敏()
    {
        var args = new Dictionary<string, object?>
        {
            ["password"] = "super-secret-value",
            ["payload"] = new string('x', 5000),
        };

        var json = SmartAdmin.AspNetCore.SensitiveDataMasker.Mask(args, maxChars: 500);

        Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);
    }
}
