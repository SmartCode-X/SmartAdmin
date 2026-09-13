namespace SmartAdmin.Core;

/// <summary>
/// 雪花 ID 配置(对应 <c>SmartAdmin:Id</c> 节)。
/// </summary>
public class AdminIdOptions
{
    /// <summary>
    /// 机器号(0–63)。<c>null</c>(默认)= 未显式配置 → 启动时用<b>文件锁</b>在本机抢一个空闲槽位
    /// (<see cref="WorkerIdLease"/>):同机多进程(IIS 应用池重叠回收、Web 园、同机多份部署)各得其号,
    /// 结构上不可能同号。显式给值 = 固定用它,不抢锁。对应 <c>SmartAdmin:Id:WorkerId</c>。
    /// <para><b>跨机器/跨容器水平扩展时必须为每个实例显式配置不同值</b>(各自文件系统独立,文件锁管不到),
    /// 否则不同实例同毫秒发号会撞 Id(主键冲突/数据错插)。</para>
    /// <para>之所以要能区分"没配"与"配成 0":在<b>明显的多实例意图</b>(<c>Cache:Provider=Redis</c>)下
    /// 没给机器号时<b>启动即抛</b>——把一个静默的主键冲突换成一条可读的启动错误。显式写 <c>0</c> 即视为运维已知情,放行。</para>
    /// <para>运行期从 DI 注入本类型读到的是<b>有效机器号</b>:自动抢号的结果在首次解析时回写到这里,
    /// 雪花发号器、任务节点名、文件日志后缀、数据库租约守卫读到的是同一个值。别绕过 DI 直接读
    /// <c>SmartAdminOptions.Id</c>,那可能还是回写前的 <c>null</c>。</para>
    /// </summary>
    public int? WorkerId { get; set; }

    /// <summary>
    /// 机器号锁目录,仅在未显式配置 <see cref="WorkerId"/> 时使用;空 = <see cref="WorkerIdLease.DefaultLockDir"/>
    /// (Windows 为 <c>%ProgramData%\SmartAdmin\workerid</c>,其它系统为 <c>/tmp/smartadmin/workerid</c>)。
    /// 对应 <c>SmartAdmin:Id:WorkerIdLockDir</c>。
    /// <para><b>必须是机器级的绝对路径</b>:若跟着程序目录走,同机上两份不同部署会各自抢到 0 号,等于没锁。
    /// 只在默认目录写不了(运行账户无权限、只读文件系统)时才需要改它。</para>
    /// </summary>
    public string? WorkerIdLockDir { get; set; }
}
