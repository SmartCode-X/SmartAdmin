namespace SmartAdmin.Core;

/// <summary>数据库配置(对应 appsettings 的 SmartAdmin:Database)</summary>
public class AdminDatabaseOptions
{
    /// <summary>Sqlite | MySql | SqlServer | PostgreSQL</summary>
    public string DbType { get; set; } = "Sqlite";

    /// <summary>
    /// 连接串,默认 <c>Data Source=./data/SmartAdmin.db</c>(SQLite)。
    /// SQLite 下相对 <c>Data Source</c> 路径按 <c>ContentRoot</c> 规整为绝对路径(装配时自动创建父目录);
    /// 其他方言按各自驱动的连接串语法原样使用。
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=./data/SmartAdmin.db";

    /// <summary>首启是否 CodeFirst 自动建表</summary>
    public bool EnableCodeFirst { get; set; } = true;

    /// <summary>
    /// 生产环境是否允许 CodeFirst 自动建表/改表(建表安全闸门)。
    /// <b>默认 false</b>:生产库通常由 DBA 手工维护,应用不应擅自 ALTER 表结构。即使 <see cref="EnableCodeFirst"/> 为 true,
    /// 生产环境(<c>IHostEnvironment.IsProduction()</c>)也需本项显式为 true 才建表;非生产环境不受此约束。
    /// </summary>
    public bool EnableCodeFirstInProduction { get; set; }

    /// <summary>
    /// 是否放行 CodeFirst 的<b>破坏性</b>变更。<b>默认 false</b>:建表前先比对实体与库里的列,
    /// 发现"实体没声明的列会被删、字符串长度收窄、可空改非空、文本与数值/时间互换"这几类会丢数据的差异,
    /// 就列出清单并拒绝启动;确认清单无误后本项置 true 放行(通常只开这一次)。
    /// <para>SQLite 例外:它的 CodeFirst 只加列,这些差异只打 Warning 不拦。</para>
    /// <para>对应 <c>SmartAdmin:Database:AllowDestructiveSchemaChange</c>。</para>
    /// </summary>
    public bool AllowDestructiveSchemaChange { get; set; }

    /// <summary>
    /// CodeFirst 版本号(可选)。<b>不配 = 每次启动都跑一遍 CodeFirst 全表扫描</b>:SqlSugar 逐表比对列定义,
    /// 表多、库在远端时启动会明显变慢。
    /// <para>配了以后:建表成功时把它记进 <c>sys_schema_version</c>(Id=2),下次启动发现没变、且实体表都在,
    /// 就跳过整个扫描。实体改了(加表、加列、改长度)就改这个号,例如用日期 <c>2026.09.06</c>。
    /// 内核改表结构时会升自己的架构版本,它和这个号并在同一个键里比对,不用你操心。
    /// 加了新表却忘了改号有兜底(缺表照样建);改了列忘了改号则不会补列,所以改实体时顺手改它。</para>
    /// <para>最长 24 个字符。对应 <c>SmartAdmin:Database:CodeFirstVersion</c>。</para>
    /// </summary>
    public string? CodeFirstVersion { get; set; }

    /// <summary>首启是否写种子</summary>
    public bool EnableSeed { get; set; } = true;

    /// <summary>
    /// 慢 SQL 告警阈值(毫秒):执行耗时 ≥ 本值的语句,连同 SQL 与参数打一条 <c>Warning</c>。
    /// <para><b>≤ 0 = 关闭</b>(只保留失败 SQL 的 <c>Error</c> 日志,那条不受本项控制 —— 查询失败却打不出 SQL,
    /// 线上就没法查了,不给关的开关)。</para>
    /// <para>默认 1000ms:够慢才值得看。调小(如 1)可用来观察全部语句,但生产上会把日志淹掉。</para>
    /// </summary>
    public int SlowSqlMillis { get; set; } = 1000;

    /// <summary>
    /// SQL 控制台日志(开发期看 ORM 生成了什么、每条多久)。默认关,见 <see cref="AdminSqlLogOptions"/>。
    /// <para>与 <see cref="SlowSqlMillis"/> 是两件事:慢 SQL 是生产告警(只打超阈值的那几条),
    /// 本项是开发期全量观察。都开时同一条语句只打一次,取较高的级别。</para>
    /// </summary>
    public AdminSqlLogOptions SqlLog { get; set; } = new();
}
