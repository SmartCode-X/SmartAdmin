namespace SmartAdmin.SqlSugar;

/// <summary>
/// 种子数据非泛型标记——供 DI 统一收集(<c>GetServices&lt;ISeedData&gt;()</c>)。
/// 请勿直接实现本接口,实现泛型版 <see cref="ISeedData{TEntity}"/>。
/// </summary>
public interface ISeedData;

/// <summary>
/// 种子数据契约(扩展点)。首次启动自动执行、重复启动幂等。
/// <para><b>幂等规则:按主键判存,只插不存在的行、绝不更新已存在的行</b>——
/// 用户在界面上改过的数据不会被下次重启覆盖回种子值。
/// 唯一例外是 <see cref="ISeedData{TEntity}.SyncOnUpgrade"/>(默认 false,见其注释)。
/// 因此种子实体<b>必须显式给定固定 Id</b>,且必须落在保留区间内(见 <see cref="SmartAdmin.Core.SmartSeedIds"/>:
/// 内核占 <c>[1, 999]</c>,<b>消费者从 1000 起</b>;上限不是写死的数字,而是启动时刻动态算出的雪花地板——
/// 严格小于它就永远不会被此后真实产生的雪花号撞上,详见 <see cref="SmartAdmin.Core.SnowflakeIdGenerator.CurrentFloor"/>)。
/// 越界与 Id=0 都会被启动检查直接拒绝。</para>
/// <para>返回空集合是合法的:据此可实现"库里已有数据就不播种"(内置 <c>SuperAdminSeed</c> 正是这么做的)。</para>
/// <para>用法(用户侧一样)——注意必须实现<b>泛型</b>版,非泛型 <see cref="ISeedData"/> 只是 DI 收集用的空标记:</para>
/// <code>
/// public class DeviceSeedData : ISeedData&lt;Device&gt;
/// {
///     public IEnumerable&lt;Device&gt; HasData() =&gt; [ new() { Id = SmartSeedIds.ConsumerMin, Name = "示例设备" } ];
/// }
///
/// // 在你自己的 Program.cs 里注册(按实现类型防重):
/// builder.Services.TryAddEnumerable(ServiceDescriptor.Transient&lt;ISeedData, DeviceSeedData&gt;());
/// </code>
/// <para>实现类支持构造注入(如 <c>IPasswordHasher</c>、<c>SmartAdminOptions</c>),注册为 <c>ISeedData</c> 的 DI 多实现。
/// <b>内核不扫描程序集找种子</b>——框架种子与用户种子都得显式注册(<c>options.ApplicationAssemblies</c> 只管实体建表与控制器挂载,
/// 不管种子)。忘了注册的后果是种子<b>静默不执行</b>,没有任何报错。</para>
/// </summary>
public interface ISeedData<out TEntity> : ISeedData where TEntity : PrimaryId, new()
{
    /// <summary>返回应当存在的种子行(带固定 Id)。启动时与库中现状按主键比对,缺哪行插哪行。</summary>
    IEnumerable<TEntity> HasData();

    /// <summary>
    /// 结构性种子:内核种子版本(<see cref="SysSchemaVersion.Current"/>)变化时,把库中已有的同 Id 行
    /// <b>覆盖回种子值</b>。默认 <c>false</c> = 只插不改(见上文幂等规则)。
    /// <para>解决的问题:新增种子行会自然流到老库,但<b>改动已有行</b>(挪菜单挂载点、给模块补图标)不会——
    /// 老库永远停在旧结构上。开了这个开关,内核升级那一次会把它们刷成新结构;平时重启仍然一行都不碰。</para>
    /// <para><b>只有内核拥有的结构能开</b>(菜单树 <c>DefaultMenuSeed</c> / 模块 <c>DefaultModuleSeed</c>)。
    /// 凡是用户会在界面上改的数据——字典、用户(密码!)、角色(授权)——<b>绝不能整表开</b>,
    /// 否则用户存过的值会在升级时被静默回滚。这条边界是本机制的全部安全性所在。
    /// 配置中心(<c>sys_config</c>)与菜单树都靠 <see cref="SyncColumns"/> 把可覆盖列锁在内核拥有的那几列:
    /// 配置只刷展示名/分组/排序/备注,<c>ConfigValue</c> 永不在内;菜单只刷挂载点/类型/权限码/路由/组件/图标/所属应用,
    /// 用户在界面上改的标题/排序/可见/启用留着。</para>
    /// <para>代价:不给 <see cref="SyncColumns"/> 而整行开同步的种子,用户对<b>内置行</b>的界面改动会在内核升级时丢失。
    /// 审计字段与软删标记不受影响(覆盖时排除)。</para>
    /// </summary>
    bool SyncOnUpgrade => false;

    /// <summary>
    /// 判存所依据的业务唯一键列(默认 <c>null</c> = 按主键 <c>Id</c> 判存)。
    /// <para>用于<b>连接表</b>种子(如 <c>sys_user_role</c> / <c>sys_role_data_scope</c>):这类表的代理主键
    /// 是运行时发的雪花号、会随「角色页增删授权」漂移,真正的身份是唯一索引列。若仍按主键 Id 判存,
    /// 运行时改过授权后重启,种子会拿固定 Id 把同一个业务键再插一遍 → <b>撞唯一索引 → 启动失败</b>。
    /// 声明业务键后,判存看的是业务键:该键已存在就跳过(无论它挂的是种子 Id 还是雪花 Id)。</para>
    /// <para>列名用 <c>nameof</c> 给出以防重命名失配。声明了业务键的种子不参与 <see cref="SyncOnUpgrade"/>
    /// (连接表没有可覆盖的业务字段,判存跳过即全部语义)。</para>
    /// </summary>
    string[]? DedupColumns => null;

    /// <summary>
    /// 升级同步时<b>只覆盖这几列</b>(须与 <see cref="SyncOnUpgrade"/> 同开);<c>null</c> = 覆盖除审计、软删标记、
    /// 机构锚点之外的全部列。
    /// <para>为的是让"用户会改的表"也能安全同步一部分:配置中心的展示名、分组、排序、备注属于内核拥有的
    /// 元信息,值(<c>ConfigValue</c>)才是用户的。列白名单把这条边界写进代码,而不是靠"这张表整体别开"。</para>
    /// </summary>
    string[]? SyncColumns => null;
}
