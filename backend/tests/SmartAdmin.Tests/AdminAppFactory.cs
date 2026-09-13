namespace SmartAdmin.Tests;

/// <summary>
/// 内核自己的集成测试工厂:<c>SmartAdmin.Testing</c> 包里的 <see cref="Testing.AdminAppFactory{TEntryPoint}"/>
/// 套在 <c>SmartAdmin.TestHost</c>(一个"用户 App")上。与包的默认只差一点:禁用内置 Dict 模块,
/// 让 TestHost 的自定义字典控制器接管其路由(可替换性用例的前提)。
/// </summary>
public sealed class AdminAppFactory : Testing.AdminAppFactory<Program>
{
    public AdminAppFactory() => DisabledModules = ["Dict"];

    /// <summary>模板库也按本工厂的默认形态建(禁 Dict),与用例看到的宿主完全一致。</summary>
    protected override IDisposable StartTemplateHost(string identity)
    {
        var host = new AdminAppFactory { DbPath = identity, FreshDatabase = true, DeleteDbOnDispose = false };
        _ = host.CreateClient();
        return host;
    }
}
