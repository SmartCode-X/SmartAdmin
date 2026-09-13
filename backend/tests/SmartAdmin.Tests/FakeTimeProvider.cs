namespace SmartAdmin.Tests;

/// <summary>
/// 可推进的测试时钟。内核不引 <c>Microsoft.Extensions.TimeProvider.Testing</c>(测试项目也跟着不引),
/// 手写这一个即够:构造给起点,<see cref="Advance"/> 往前推。
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _utc = start;

    public override DateTimeOffset GetUtcNow() => _utc;

    public void Advance(TimeSpan delta) => _utc += delta;
}
