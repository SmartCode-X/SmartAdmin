using System.Net.Http.Headers;
using SmartAdmin.Core;
using SmartAdmin.TestHost;

namespace SmartAdmin.Tests;

/// <summary>
/// 消费者错误码目录:登记后 <c>(ErrorCode)MyCodes.X</c> 的 msgKey 解析到消费者自己的 <c>[MsgKey]</c>。
/// <para>没有这条能力时,消费者只能整段复制内核的异常过滤器再补一张自己的码表——真实消费方就是这么做的。</para>
/// </summary>
public class ErrorCodeRegistryTests
{
    /// <summary>TestHost 已登记为 ApplicationAssemblies,其 SampleErrorCode 应被装配时自动扫入目录。</summary>
    [Fact]
    public void Consumer_enum_in_application_assembly_is_registered_automatically()
    {
        using var f = new AdminAppFactory();
        _ = f.CreateClient();   // 触发装配

        Assert.Equal("error.sample.widgetBusy", ErrorCodeRegistry.GetMsgKey(60001));
        Assert.Equal("error.sample.widgetBusy", ((ErrorCode)SampleErrorCode.WidgetBusy).GetMsgKey());
        Assert.Equal("error.sample.widgetBusy", new AdminException((ErrorCode)SampleErrorCode.WidgetBusy).MsgKey);
    }

    /// <summary>端到端:消费者码抛出后,HTTP 信封里的 msgKey 就是消费者那一份。</summary>
    [Fact]
    public async Task Consumer_code_reaches_the_envelope_with_its_own_msgkey()
    {
        using var f = new AdminAppFactory();
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));

        var env = await (await c.GetAsync("/api/v1/diag/throw-consumer-code")).ReadEnvelope();

        Assert.Equal(60001, env.GetProperty("code").GetInt32());
        Assert.Equal("error.sample.widgetBusy", env.GetProperty("msgKey").GetString());
    }

    [Fact]
    public void Kernel_codes_are_registered_out_of_the_box()
    {
        Assert.Equal("error.auth.passwordWrong", ErrorCodeRegistry.GetMsgKey(40001));
        Assert.True(ErrorCodeRegistry.TryGet(40001, out var d));
        Assert.Equal(nameof(ErrorCode.PasswordWrong), d.Name);
        Assert.Contains(ErrorCodeRegistry.All, x => x.Code == 40001);
    }

    [Fact]
    public void Unregistered_code_falls_back_to_numeric_key()
    {
        Assert.Equal("error.code.987654", ErrorCodeRegistry.GetMsgKey(987654));
    }

    /// <summary>两个枚举抢同一个数值 = 前端按码查文案必然错一个,登记时就点名两边拒绝。</summary>
    [Fact]
    public void Conflicting_code_value_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ErrorCodeRegistry.Register(typeof(ConflictingCodes)));
        Assert.Contains("40001", ex.Message);
        Assert.Contains(nameof(ErrorCode.PasswordWrong), ex.Message);
    }

    [Fact]
    public void Registering_the_same_enum_twice_is_idempotent()
    {
        ErrorCodeRegistry.Register(typeof(IdempotentCodes));
        var before = ErrorCodeRegistry.All.Count;
        ErrorCodeRegistry.Register(typeof(IdempotentCodes));
        Assert.Equal(before, ErrorCodeRegistry.All.Count);
    }

    [Fact]
    public void Non_enum_and_keyless_enum_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ErrorCodeRegistry.Register(typeof(string)));
        Assert.Throws<ArgumentException>(() => ErrorCodeRegistry.Register(typeof(KeylessCodes)));
    }

    /// <summary>与内核 PasswordWrong 同码不同键,用来验冲突检测。</summary>
    private enum ConflictingCodes
    {
        [MsgKey("error.conflicting.other")]
        Other = 40001,
    }

    private enum IdempotentCodes
    {
        [MsgKey("error.idempotent.one")]
        One = 61001,
    }

    private enum KeylessCodes
    {
        One = 62001,
    }
}
