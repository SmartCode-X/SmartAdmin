using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// <see cref="AdminException"/> 的 innerException 重载:包装外部调用失败时保住原始那一层,
/// 而对外的信封一个字不变(code/msgKey/args/message 都不受影响)。
/// </summary>
public class AdminExceptionTests
{
    [Fact]
    public void Inner_exception_is_kept_and_envelope_fields_are_unchanged()
    {
        var inner = new IOException("磁盘已满");
        var args = new Dictionary<string, object?> { ["max"] = 100 };

        var ex = new AdminException(ErrorCode.SystemError, inner, args, "上传失败");

        Assert.Same(inner, ex.InnerException);
        Assert.Equal(ErrorCode.SystemError, ex.Code);
        Assert.Equal(ErrorCode.SystemError.GetMsgKey(), ex.MsgKey);
        Assert.Equal(args, ex.Args);
        Assert.Equal("上传失败", ex.Message);

        // 信封只带码与文案:原始异常仅供日志,不能顺着响应漏给调用方。
        var envelope = Result<object>.From(ex);
        Assert.Equal((int)ErrorCode.SystemError, envelope.Code);
        Assert.Equal(ErrorCode.SystemError.GetMsgKey(), envelope.MsgKey);
        Assert.Equal("上传失败", envelope.Message);
        Assert.Equal(args, envelope.Args);
    }

    /// <summary>不传 innerException 时:Message 兜底为 msgKey,InnerException 为空。</summary>
    [Fact]
    public void Existing_constructor_behaviour_is_untouched()
    {
        var ex = new AdminException(ErrorCode.PasswordWrong);

        Assert.Null(ex.InnerException);
        Assert.Equal(ErrorCode.PasswordWrong.GetMsgKey(), ex.Message);
        Assert.Null(ex.Args);
    }
}
