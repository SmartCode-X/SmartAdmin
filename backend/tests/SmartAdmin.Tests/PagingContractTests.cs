using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// 分页上限契约:超限抛错而不是静默截断。
/// <para>若超限时悄悄按上限查——拿分页当导出用的地方会以为导全了,实际只拿到前 200 行,而且没有任何信号。</para>
/// <para>集合串行:<c>PagedListExtensions.MaxSize</c> 是进程级静态值,与并发跑的 HTTP 用例互相干扰。</para>
/// </summary>
[Collection(nameof(PagingContractTests))]
[CollectionDefinition(nameof(PagingContractTests), DisableParallelization = true)]
public class PagingContractTests
{
    [Fact]
    public async Task Size_over_limit_throws_instead_of_truncating()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var env = await (await c.GetAsync("/api/v1/sys/role/page?Current=1&Size=201")).ReadEnvelope();

        Assert.Equal((int)ErrorCode.PageSizeExceeded, env.GetProperty("code").GetInt32());
        Assert.Equal(200, env.GetProperty("args").GetProperty("max").GetInt32());
        Assert.Equal(201, env.GetProperty("args").GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task Size_at_limit_still_works()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var env = await (await c.GetAsync("/api/v1/sys/role/page?Current=1&Size=200")).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
    }

    /// <summary>上限可配:配了多大就允许多大。</summary>
    [Fact]
    public void MaxPageSize_option_drives_the_static_limit()
    {
        var original = PagedListExtensions.MaxSize;
        try
        {
            new ServiceCollection().AddSmartAdminOptions(new SmartAdminOptions { Api = { MaxPageSize = 333 } });
            Assert.Equal(333, PagedListExtensions.MaxSize);
        }
        finally
        {
            PagedListExtensions.MaxSize = original;
        }
    }

    [Fact]
    public void Non_positive_max_page_size_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSmartAdminOptions(new SmartAdminOptions { Api = { MaxPageSize = 0 } }));
        Assert.Contains("MaxPageSize", ex.Message);
    }

    /// <summary>PageInputBase 是具体类(非抽象):没有额外过滤条件的端点可以直接拿它当 [FromQuery] 入参。</summary>
    [Fact]
    public async Task PageInputBase_binds_directly_as_query_input()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var env = await (await c.GetAsync("/api/v1/diag/page-echo?Current=2&Size=37")).ReadEnvelope();

        Assert.Equal(0, env.GetProperty("code").GetInt32());
        Assert.Equal(37, env.GetProperty("data").GetInt32());
    }
}

/// <summary>
/// 事务包装:失败时回滚并把原异常原样抛出,不吞进 DbResult。
/// <para>吞异常正是消费者自己重写一份事务助手的原因——业务异常到不了统一异常过滤器,就变成了 500。</para>
/// </summary>
public class SqlSugarTransactionTests
{
    [Fact]
    public async Task Business_exception_propagates_and_work_is_rolled_back()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>();
        var code = $"tran-{Guid.NewGuid():N}"[..16];

        var ex = await Assert.ThrowsAsync<AdminException>(() =>
            roles.ExecuteInTransactionAsync(async () =>
            {
                await roles.InsertAsync(new SysRole { Name = "事务用例", Code = code, Enabled = true });
                throw new AdminException(ErrorCode.RoleNotFound);
            }));

        Assert.Equal(ErrorCode.RoleNotFound, ex.Code);   // 原异常原样冒泡,不是 DbResult 包装
        Assert.False(await roles.AnyAsync(r => r.Code == code));   // 已回滚
    }

    [Fact]
    public async Task Successful_work_is_committed()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>();
        var code = $"tran-{Guid.NewGuid():N}"[..16];

        await roles.ExecuteInTransactionAsync(async () =>
            await roles.InsertAsync(new SysRole { Name = "事务用例", Code = code, Enabled = true }));

        Assert.True(await roles.AnyAsync(r => r.Code == code));
    }

    [Fact]
    public async Task GetRequiredAsync_throws_the_given_code_when_absent()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>();

        var ex = await Assert.ThrowsAsync<AdminException>(() => roles.GetRequiredAsync(9_999_999, ErrorCode.RoleNotFound));
        Assert.Equal(ErrorCode.RoleNotFound, ex.Code);

        var existing = await roles.GetRequiredAsync(1, ErrorCode.RoleNotFound);
        Assert.Equal(1, existing.Id);
    }

    /// <summary>软删行仍占唯一索引,唯一性校验必须看得见它,否则插入时才撞库。</summary>
    [Fact]
    public async Task AnyIncludingDeletedAsync_sees_soft_deleted_rows()
    {
        using var f = new AdminAppFactory();
        using var scope = f.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRepository<SysRole>>();
        var code = $"del-{Guid.NewGuid():N}"[..16];

        await roles.InsertAsync(new SysRole { Name = "待删角色", Code = code, Enabled = true });
        var row = await roles.GetFirstAsync(r => r.Code == code);
        await roles.DeleteAsync(row!.Id);

        Assert.False(await roles.AnyAsync(r => r.Code == code));                 // 普通查询看不见
        Assert.True(await roles.AnyIncludingDeletedAsync(r => r.Id == row.Id));  // 唯一性校验看得见
    }
}
