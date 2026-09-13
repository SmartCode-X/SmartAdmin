namespace SmartAdmin.Core;

/// <summary>
/// SQL 控制台日志(对应 <c>SmartAdmin:Database:SqlLog</c>)。
/// <para><b>默认关</b>,开发期开着看 ORM 到底生成了什么、每条跑了多久。开启后每条语句打一个多行块
/// (语句 + 参数已内联 + 耗时),请求结束再打一行汇总(条数/合计耗时/最慢那条),N+1 一眼可见。</para>
/// <para>通道是 <c>ILogger("SmartAdmin.Sql")</c>,所以 <c>Logging:LogLevel:SmartAdmin.Sql</c> 也能单独调级别,
/// 文件日志同样收得到。<b>生产别开</b>:每请求若干条语句,日志量与 IO 都不是给生产准备的。</para>
/// </summary>
public class AdminSqlLogOptions
{
    /// <summary>总开关,默认关。</summary>
    public bool Enabled { get; set; }

    /// <summary>只打耗时 ≥ 本值(毫秒)的语句;<b>0 = 全打</b>(默认)。想只看慢的就调大。</summary>
    public int MinMillis { get; set; }

    /// <summary>单条语句打印的最大字符数,超出截断。默认 4000——批量插入的 SQL 能有几十万字符。</summary>
    public int MaxSqlChars { get; set; } = 4000;

    /// <summary>
    /// 每个 HTTP 请求 / 任务触发结束后是否打一行汇总(条数、合计耗时、最慢一条)。默认开。
    /// <para>这行才是查 N+1 的入口:单看语句只觉得"每条都很快",汇总里"一个请求 143 条"才刺眼。</para>
    /// </summary>
    public bool RequestSummary { get; set; } = true;

    /// <summary>
    /// 是否加 ANSI 颜色。<b>null = 自动</b>:stdout 是终端且未设 <c>NO_COLOR</c> 才加。
    /// 同时开着文件日志时建议显式 false,否则文件里会混进转义序列。
    /// </summary>
    public bool? Color { get; set; }
}
