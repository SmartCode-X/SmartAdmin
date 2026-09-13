namespace SmartAdmin.SqlSugar;

/// <summary>
/// 库就绪钩子:建表与种子都跑完之后、其余托管服务启动之前调用,按注册顺序执行,抛异常即启动失败(与种子同级)。
/// <para>解决的是注册顺序陷阱:消费者想"等表建好、种子播完再初始化自己的数据",若自己写一个
/// <c>IHostedService</c>,必须注册在 <c>AddSmartAdmin()</c> 之后——托管服务按注册顺序启动,
/// 注册早了就会在表还不存在时插数据,而这条约束没有任何东西提醒你。</para>
/// <para>用 <c>TryAddEnumerable</c> 登记,可注入 Scoped 依赖(与种子共用同一个作用域)。</para>
/// </summary>
public interface IDatabaseReadyHook
{
    /// <summary>库已就绪时调用。</summary>
    Task OnDatabaseReadyAsync(DatabaseReadyContext context, CancellationToken cancellationToken);
}

/// <summary>库就绪时的现场信息:本次启动做了什么。</summary>
/// <param name="CodeFirstRan">本次是否真的跑了 CodeFirst 建表(配置关闭或版本未变跳过时为 false)</param>
/// <param name="SeedRan">本次是否执行了种子(<c>EnableSeed=false</c> 时为 false)</param>
/// <param name="Upgraded">本次是否是一次种子版本升级(空库首启也算)</param>
/// <param name="PreviousSchemaVersion">升级前库里的种子版本;空库为 null</param>
/// <param name="CurrentSchemaVersion">内核当前的种子版本</param>
public sealed record DatabaseReadyContext(
    bool CodeFirstRan,
    bool SeedRan,
    bool Upgraded,
    string? PreviousSchemaVersion,
    string CurrentSchemaVersion);
