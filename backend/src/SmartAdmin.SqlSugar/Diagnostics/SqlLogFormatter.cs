using System.Globalization;
using System.Text;
using SmartAdmin.Core;
using SqlSugar;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// SQL 控制台日志的渲染。参数<b>内联进语句</b>——排查时要的是一条能直接贴进客户端跑的 SQL,
/// 而不是"语句在这里、参数在那里"再人肉拼。
/// <para>不用 SqlSugar 自带的 <c>UtilMethods.GetSqlString</c>:它会把口令哈希、TOTP 种子这类值原样吐出来。
/// 这里按参数名过 <see cref="SensitiveKeys"/>,命中的只打 <c>'***'</c>。</para>
/// <para>颜色用 ANSI 码嵌进消息串,<b>不用 <c>Console.ForegroundColor</c></b>:那是进程级状态,
/// 多线程并发写日志时颜色会串到别的行上。</para>
/// </summary>
public static class SqlLogFormatter
{
    private const string RESET = "\u001b[0m";

    /// <summary>语句块的消息模板。字面部分(框线)在模板里,颜色随参数值走,结构化 sink 仍拿得到各字段。</summary>
    public const string BLOCK_TEMPLATE = "┌ SQL {Index} {Kind} [{ConfigId}] {Elapsed}\n│ {Sql}\n└ {Params}";

    /// <summary>汇总行模板。</summary>
    public const string SUMMARY_TEMPLATE = "└ SQL 汇总 {Label} → {Count} 条 / {Total} / 最慢 {Slowest}{Hint}";

    /// <summary>一个请求里超过这么多条语句就提示疑似 N+1。</summary>
    private const int N_PLUS_ONE_HINT = 20;

    /// <summary>是否加颜色:显式配了就听配置,没配则 stdout 是终端且未设 NO_COLOR 才加。</summary>
    public static bool ShouldColor(bool? configured) => configured
        ?? (!Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));

    /// <summary>语句块的六个参数值(按 <see cref="BLOCK_TEMPLATE"/> 的顺序)。</summary>
    public static object?[] BlockArgs(
        int? index, string sql, object? parameters, double millis, string configId, int maxSqlChars, bool color, int slowMillis)
    {
        var (kind, kindColor) = Classify(sql);
        var inlined = Inline(sql, parameters, maxSqlChars, out var paramCount, out var truncated);

        var note = paramCount == 0 ? "无参数" : $"参数 {paramCount} 个(已内联)";
        if (truncated) note += $" · 语句超 {maxSqlChars} 字已截断";

        return
        [
            index is null ? "" : "#" + index.Value.ToString(CultureInfo.InvariantCulture),
            Paint(kind, kindColor, color),
            configId,
            Paint(millis.ToString("F1", CultureInfo.InvariantCulture) + " ms", ElapsedColor(millis, slowMillis), color),
            inlined,
            note,
        ];
    }

    /// <summary>汇总行的五个参数值(按 <see cref="SUMMARY_TEMPLATE"/> 的顺序)。</summary>
    public static object?[] SummaryArgs(SqlLogStats stats, bool color)
    {
        var hint = stats.Count >= N_PLUS_ONE_HINT
            ? Paint($"  ← {stats.Count} 条,疑似 N+1", "\u001b[33m", color)
            : "";
        return
        [
            stats.Label,
            stats.Count,
            stats.TotalMillis.ToString("F1", CultureInfo.InvariantCulture) + " ms",
            stats.Count == 0 ? "-" : $"#{stats.SlowestIndex} {stats.SlowestMillis.ToString("F1", CultureInfo.InvariantCulture)} ms",
            hint,
        ];
    }

    /// <summary>按首个关键字分类,顺带给一个颜色。</summary>
    private static (string Kind, string Color) Classify(string sql)
    {
        var head = sql.AsSpan().TrimStart();
        // 带 CTE 的查询以 WITH 开头,别归到"其它"里去
        if (StartsWith(head, "SELECT") || StartsWith(head, "WITH")) return ("查询", "\u001b[32m");
        if (StartsWith(head, "INSERT")) return ("新增", "\u001b[33m");
        if (StartsWith(head, "UPDATE")) return ("修改", "\u001b[34m");
        if (StartsWith(head, "DELETE")) return ("删除", "\u001b[31m");
        if (StartsWith(head, "CREATE") || StartsWith(head, "ALTER")
            || StartsWith(head, "DROP") || StartsWith(head, "TRUNCATE")) return ("建表", "\u001b[35m");
        return ("其它", "\u001b[90m");
    }

    private static bool StartsWith(ReadOnlySpan<char> text, string word) =>
        text.StartsWith(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>耗时配色:快的绿、够得着慢 SQL 阈值一半的黄、越线的红。</summary>
    private static string ElapsedColor(double millis, int slowMillis)
    {
        var slow = slowMillis > 0 ? slowMillis : 1000;
        if (millis >= slow) return "\u001b[31m";
        return millis >= slow / 2.0 ? "\u001b[33m" : "\u001b[32m";
    }

    private static string Paint(string text, string color, bool enabled) =>
        enabled ? color + text + RESET : text;

    /// <summary>
    /// 把参数值替换进语句。按参数名<b>从长到短</b>替换:否则 <c>@p1</c> 会先把 <c>@p10</c> 咬掉半截。
    /// </summary>
    private static string Inline(
        string sql, object? parameters, int maxChars, out int paramCount, out bool truncated)
    {
        var pars = parameters as SugarParameter[] ?? [];
        paramCount = pars.Length;

        var text = sql;
        foreach (var p in pars.OrderByDescending(p => p.ParameterName?.Length ?? 0))
        {
            var name = p.ParameterName;
            if (string.IsNullOrEmpty(name)) continue;
            var literal = SensitiveKeys.IsSensitive(name) ? "'***'" : Literal(p.Value);
            text = text.Replace(name, literal, StringComparison.Ordinal);
            // 参数名有时不带符号,而语句里带着;两种写法都试一遍
            if (name[0] is not ('@' or ':' or '?'))
                text = text.Replace("@" + name, literal, StringComparison.Ordinal);
        }

        truncated = text.Length > maxChars;
        return truncated ? string.Concat(text.AsSpan(0, maxChars), " …") : text;
    }

    /// <summary>值渲染成 SQL 字面量。求的是可读且大多数情况可直接执行,不追求四种方言全都能跑。</summary>
    private static string Literal(object? value) => value switch
    {
        null or DBNull => "NULL",
        // bool 各方言写法不一(SqlServer 的 bit 不吃 TRUE),统一 1/0:三种方言可直接跑,PG 上要自己改一下
        bool b => b ? "1" : "0",
        string s => Quote(s),
        char c => Quote(c.ToString()),
        Guid g => Quote(g.ToString()),
        DateTime d => Quote(d.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
        DateTimeOffset d => Quote(d.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)),
        byte[] bytes => $"0x{Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 16)))}({bytes.Length} 字节)",
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? ""),
    };

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('\'');
        foreach (var c in s)
        {
            if (c == '\'') sb.Append('\'');      // SQL 里单引号靠重复转义
            sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }
}
