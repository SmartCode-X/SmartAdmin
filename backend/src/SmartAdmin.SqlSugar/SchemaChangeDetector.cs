using SqlSugar;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// CodeFirst 执行前的破坏性差异检测:拿实体的列定义与库里现有列比对,挑出 SqlSugar 一旦执行就会丢数据的那几类。
/// <para>SqlSugar 的 <c>InitTables</c> 对已存在的表不只加列:实体没声明的列会被 <c>DROP</c>,长度收窄、
/// 可空改非空、类型变化都会原样 <c>ALTER</c>(SQLite 例外——它的 CodeFirst 只加列)。这里只判"确定会丢数据"的形状,
/// 拿不准的一律放行:误拦一个今天能正常跑的库,是新的失败面。</para>
/// </summary>
internal static class SchemaChangeDetector
{
    /// <summary>一处破坏性差异:哪张表、哪一列、什么类型的变更、库里现状 → 实体要求。</summary>
    internal sealed record Change(string Table, string Column, string Kind, string Detail)
    {
        public override string ToString() => $"{Table}.{Column}: {Kind}({Detail})";
    }

    internal const string KindDrop = "删列";
    internal const string KindNarrow = "收窄";
    internal const string KindNotNull = "改为非空";
    internal const string KindType = "类型不兼容";

    /// <summary>
    /// 比对一张表。<paramref name="entityColumns"/> 里 <c>IsIgnore</c> 的列不参与;列名不分大小写;
    /// 实体声明了 <c>OldDbColumnName</c>(改名)的按新旧两个名字都算"有"。
    /// </summary>
    internal static List<Change> Diff(string table, IEnumerable<EntityColumnInfo> entityColumns, IEnumerable<DbColumnInfo> dbColumns)
    {
        var changes = new List<Change>();
        var entities = entityColumns.Where(c => !c.IsIgnore).ToList();
        var db = dbColumns.ToList();

        // 1) 库里有、实体没有 → SqlSugar 会 DROP COLUMN
        foreach (var dc in db)
        {
            var declared = entities.Any(ec =>
                Same(ec.DbColumnName, dc.DbColumnName) || Same(ec.OldDbColumnName, dc.DbColumnName));
            if (!declared)
                changes.Add(new Change(table, dc.DbColumnName, KindDrop, Describe(dc)));
        }

        // 2) 同名列:收窄 / 改为非空 / 类型不兼容
        foreach (var ec in entities)
        {
            var dc = db.FirstOrDefault(c => Same(c.DbColumnName, ec.DbColumnName));
            if (dc is null) continue;   // 缺列是加列,不破坏

            var clrIsString = ec.UnderType == typeof(string);
            var dbIsText = IsTextType(dc.DataType);

            if (clrIsString && dbIsText && ec.Length > 0 && dc.Length > 0 && dc.Length > ec.Length)
                changes.Add(new Change(table, ec.DbColumnName, KindNarrow, $"{Describe(dc)} → {ec.Length}"));

            if (dc.IsNullable && !ec.IsNullable && !ec.IsPrimarykey && !dc.IsPrimarykey)
                changes.Add(new Change(table, ec.DbColumnName, KindNotNull, $"{Describe(dc)} NULL → NOT NULL"));

            // 只判"文本 ↔ 数值/时间"这种一眼就丢数据的换类型;Guid、二进制、JSON 这类各方言映射不一,不猜
            var clrIsScalar = IsNumericOrTemporal(ec.UnderType);
            var dbIsScalar = IsNumericOrTemporalType(dc.DataType);
            if ((clrIsString && dbIsScalar) || (clrIsScalar && dbIsText))
                changes.Add(new Change(table, ec.DbColumnName, KindType, $"{Describe(dc)} → {ec.UnderType?.Name}"));
        }

        return changes;
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Describe(DbColumnInfo dc) =>
        dc.Length > 0 && IsTextType(dc.DataType) ? $"{dc.DataType}({dc.Length})" : dc.DataType ?? "?";

    /// <summary>方言原生类型名归一:小写、去掉括号里的长度。</summary>
    private static string Norm(string? dataType)
    {
        var s = (dataType ?? "").Trim().ToLowerInvariant();
        var paren = s.IndexOf('(');
        return paren >= 0 ? s[..paren].Trim() : s;
    }

    private static readonly string[] TextTypes =
    [
        "char", "varchar", "nvarchar", "nchar", "text", "ntext", "longtext", "mediumtext", "tinytext",
        "clob", "nclob", "character varying", "character", "citext", "varchar2", "nvarchar2", "string",
    ];

    private static readonly string[] ScalarTypes =
    [
        "int", "integer", "bigint", "smallint", "tinyint", "mediumint", "int2", "int4", "int8",
        "decimal", "numeric", "number", "float", "float4", "float8", "double", "double precision", "real",
        "money", "smallmoney", "bit", "bool", "boolean",
        "date", "datetime", "datetime2", "smalldatetime", "timestamp", "timestamp without time zone",
        "timestamp with time zone", "time",
    ];

    internal static bool IsTextType(string? dataType) => TextTypes.Contains(Norm(dataType));

    internal static bool IsNumericOrTemporalType(string? dataType) => ScalarTypes.Contains(Norm(dataType));

    private static bool IsNumericOrTemporal(Type? t)
    {
        if (t is null) return false;
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum) return true;
        return t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal) || t == typeof(bool)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly)
            || t == typeof(TimeOnly) || t == typeof(TimeSpan);
    }
}
