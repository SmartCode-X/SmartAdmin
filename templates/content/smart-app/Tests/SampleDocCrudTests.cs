using System.Net.Http.Headers;
using SmartAdmin.Testing;
using Xunit;

namespace SmartApp.Tests;

/// <summary>
/// 整宿主起一遍,登录超管,把示例模块的 CRUD 从 HTTP 走到 SQLite。
/// 它守的不是业务逻辑,是接线:实体建了表没有、控制器挂上路由没有、权限码放行没有、信封包对没有。
/// 加你自己的模块时复制本文件改路由和字段即可。
/// <para>宿主、库、登录、信封解析都来自 <c>SmartAdmin.Testing</c> 包:<see cref="AppFactory"/> 只是薄薄一层;
/// 设 <c>SMART_TEST_DBTYPE=MySql</c> 等环境变量就能让同一套用例跑在别的数据库上。</para>
/// </summary>
public class SampleDocCrudTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task 示例模块_增查改删全链路()
    {
        var client = factory.CreateClient();
        var token = await client.LoginToken(AppFactory.AdminAccount, AppFactory.DefaultAdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await (await client.PostJson("/api/v1/sample/doc", new { title = "第一份文档" })).ReadEnvelope();
        Assert.Equal(0, created.GetProperty("code").GetInt32());
        var id = created.GetProperty("data").GetInt64();
        Assert.True(id > 0, "新建应返回雪花主键");

        var listed = await (await client.GetAsync("/api/v1/sample/doc")).ReadEnvelope();
        Assert.Contains(listed.GetProperty("data").EnumerateArray(),
            d => d.GetProperty("id").GetInt64() == id && d.GetProperty("title").GetString() == "第一份文档");

        var renamed = await (await client.PutJson($"/api/v1/sample/doc/{id}", new { title = "改过的标题" })).ReadEnvelope();
        Assert.True(renamed.GetProperty("data").GetBoolean());

        var deleted = await (await client.DeleteAsync($"/api/v1/sample/doc/{id}")).ReadEnvelope();
        Assert.True(deleted.GetProperty("data").GetBoolean());

        var afterDelete = await (await client.GetAsync("/api/v1/sample/doc")).ReadEnvelope();
        Assert.DoesNotContain(afterDelete.GetProperty("data").EnumerateArray(),
            d => d.GetProperty("id").GetInt64() == id);
    }

    [Fact]
    public async Task 未带令牌_被拒而不是崩()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/sample/doc");
        var body = await response.ReadEnvelope();
        Assert.NotEqual(0, body.GetProperty("code").GetInt32());
    }
}

/// <summary>
/// 测试宿主:一次性库 + 固定超管密码 + 固定 JWT 密钥,跑完即删,环境钉 <c>Development</c>——全部由基类给定。
/// 要改默认(禁用模块、额外配置、替换服务)在用例里用对象初始化器;要改工厂本身覆写 <c>ConfigureWebHost</c>。
/// </summary>
public sealed class AppFactory : AdminAppFactory<Program>
{
    public const string AdminAccount = "superAdmin";
}
