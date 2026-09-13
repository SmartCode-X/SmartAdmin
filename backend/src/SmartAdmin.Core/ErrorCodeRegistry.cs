using System.Collections.Frozen;
using System.Reflection;

namespace SmartAdmin.Core;

/// <summary>错误码目录里的一项:数值、枚举成员名、语义 msgKey、来源枚举类型。</summary>
public sealed record ErrorCodeDescriptor(int Code, string Name, string MsgKey, Type EnumType);

/// <summary>
/// 进程级错误码目录。内核 <see cref="ErrorCode"/> 自动登记;消费者把自己的错误码枚举登记进来,
/// 强转成 <see cref="ErrorCode"/> 抛出后,统一信封里的 <c>msgKey</c> 一样能解析到该枚举上的 <see cref="MsgKeyAttribute"/>。
/// <para>不登记的后果是消费者的码全部回退成 <c>error.code.{数值}</c>,前端查不到文案,只能自己维护一份
/// 异常过滤器来补这一步。登记入口有两个:<c>SmartAdminOptions.ErrorCodeEnums</c>,以及
/// <c>ApplicationAssemblies</c> 里带 <see cref="MsgKeyAttribute"/> 的枚举(装配时自动扫描)。</para>
/// </summary>
public static class ErrorCodeRegistry
{
    private static readonly object GATE = new();
    private static readonly HashSet<Type> REGISTERED = [];
    private static FrozenDictionary<int, ErrorCodeDescriptor> _byCode = FrozenDictionary<int, ErrorCodeDescriptor>.Empty;

    static ErrorCodeRegistry() => Register(typeof(ErrorCode));

    /// <summary>目录全集,按数值升序。</summary>
    public static IReadOnlyList<ErrorCodeDescriptor> All =>
        [.. _byCode.Values.OrderBy(d => d.Code)];

    /// <summary>登记一个错误码枚举(幂等)。没有任何 <see cref="MsgKeyAttribute"/> 成员即视为传错类型,直接抛。</summary>
    public static void Register<TEnum>() where TEnum : struct, Enum => Register(typeof(TEnum));

    /// <inheritdoc cref="Register{TEnum}"/>
    public static void Register(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        if (!enumType.IsEnum)
            throw new ArgumentException($"{enumType.FullName} 不是枚举类型,不能作为错误码目录登记。", nameof(enumType));

        lock (GATE)
        {
            if (!REGISTERED.Add(enumType)) return;   // 已登记过,幂等返回

            var added = enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (Field: f, Key: f.GetCustomAttribute<MsgKeyAttribute>()?.Key))
                .Where(x => x.Key is not null)
                .Select(x => new ErrorCodeDescriptor(
                    Convert.ToInt32(x.Field.GetRawConstantValue()), x.Field.Name, x.Key!, enumType))
                .ToList();

            if (added.Count == 0)
            {
                REGISTERED.Remove(enumType);
                throw new ArgumentException(
                    $"{enumType.FullName} 没有任何标注 [MsgKey] 的成员,登记它没有意义——错误码枚举的每个成员都应带语义键。",
                    nameof(enumType));
            }

            var merged = _byCode.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var item in added)
            {
                // 同数值不同 msgKey = 两个枚举抢同一个码段,前端按 code 查文案必然错一个。点名两边,启动即拒。
                if (merged.TryGetValue(item.Code, out var exist) && exist.MsgKey != item.MsgKey)
                    throw new InvalidOperationException(
                        $"错误码 {item.Code} 冲突:{exist.EnumType.Name}.{exist.Name}({exist.MsgKey}) " +
                        $"与 {item.EnumType.Name}.{item.Name}({item.MsgKey})。消费者错误码请另占码段,勿与内核或彼此重叠。");
                merged[item.Code] = item;
            }
            _byCode = merged.ToFrozenDictionary();
        }
    }

    /// <summary>取语义 msgKey;未登记的码回退 <c>error.code.{数值}</c>。</summary>
    public static string GetMsgKey(int code) =>
        _byCode.TryGetValue(code, out var d) ? d.MsgKey : $"error.code.{code}";

    /// <summary>查一个码的目录项。</summary>
    public static bool TryGet(int code, out ErrorCodeDescriptor descriptor) =>
        _byCode.TryGetValue(code, out descriptor!);
}

/// <summary>错误码目录的 DI 视图(读 <see cref="ErrorCodeRegistry"/>);给端点与消费者代码用。</summary>
public interface IErrorCodeCatalog
{
    /// <summary>目录全集,按数值升序。</summary>
    IReadOnlyList<ErrorCodeDescriptor> All { get; }

    /// <summary>取语义 msgKey;未登记的码回退 <c>error.code.{数值}</c>。</summary>
    string GetMsgKey(int code);
}

/// <inheritdoc cref="IErrorCodeCatalog"/>
public class ErrorCodeCatalog : IErrorCodeCatalog
{
    /// <inheritdoc />
    public virtual IReadOnlyList<ErrorCodeDescriptor> All => ErrorCodeRegistry.All;

    /// <inheritdoc />
    public virtual string GetMsgKey(int code) => ErrorCodeRegistry.GetMsgKey(code);
}
