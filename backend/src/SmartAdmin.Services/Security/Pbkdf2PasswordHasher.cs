using System.Security.Cryptography;
using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IPasswordHasher"/> 默认实现:PBKDF2-SHA256(BCL <see cref="Rfc2898DeriveBytes"/>,零第三方依赖)。
/// <para>产出格式(自描述,点号分隔):<c>pbkdf2-sha256.{迭代次数}.{盐Base64}.{哈希Base64}</c></para>
/// <para>自描述的意义:算法与参数存进哈希串本身——将来把迭代次数加大、甚至换算法(argon2 等),
/// 旧哈希仍按其记录的参数校验通过,用户无感;需要时可在登录成功后按新参数重哈希平滑升级。</para>
/// <para>方法 virtual:对接已有用户库时继承并只改 <see cref="Verify"/> 即可兼容旧哈希格式。</para>
/// </summary>
/// <param name="security">
/// 安全配置;取其 <c>Password</c> 子节。不传则用默认迭代次数(裸容器与老子类的构造路径不受影响)。
/// </param>
public class Pbkdf2PasswordHasher(AdminSecurityOptions? security = null) : IPasswordHasher
{
    private const string ALGORITHM = "pbkdf2-sha256";
    private const int SALT_SIZE = 16;   // 128 位随机盐
    private const int HASH_SIZE = 32;   // 256 位哈希

    /// <summary>
    /// 当前生效的迭代次数。默认 60 万(OWASP 对 PBKDF2-SHA256 的建议值),可经
    /// <c>Security:Password:Pbkdf2Iterations</c> 按硬件调节,但不低于 10 万——
    /// 一次手滑把它调到几百,等于把口令哈希削成明文级。
    /// </summary>
    protected virtual int Iterations => Math.Max(
        AdminPasswordOptions.MIN_ITERATIONS,
        security?.Password.Pbkdf2Iterations ?? 600_000);

    /// <inheritdoc />
    public virtual string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);   // 每次全新随机盐 → 同一明文两次哈希结果不同
        var iterations = Iterations;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HASH_SIZE);
        return $"{ALGORITHM}.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    public virtual bool Verify(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword)) return false;

        // 解析自描述格式;库中出现未知算法/坏格式按"不匹配"处理而非抛异常——登录路径要稳
        var parts = hashedPassword.Split('.');
        if (parts.Length != 4 || parts[0] != ALGORITHM) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }

        // 拒绝空/畸形哈希段:parts[3] 为空 → expected.Length==0 → Pbkdf2(outputLength:0) 与
        // FixedTimeEquals(空,空) 双双为真 → 任意密码通过。空盐同样无意义。畸形串一律按"不匹配"。
        // (当前写入路径恒产 32 字节段,不可达;但 Verify 可被子类覆写以兼容导入的外部/旧库哈希,这是纵深防御。)
        if (expected.Length == 0 || salt.Length == 0) return false;

        // 按哈希串里记录的参数重算(而非当前常量)→ 参数升级后旧哈希依旧可校验
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // 恒定时间比较,防时序侧信道:普通 == 会在首个不同字节提前返回,可能被用来逐字节猜出密码
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 算法段不认、或迭代次数与当前配置对不上,就该重算。<b>调小也算</b>:参数是部署的决定,
    /// 库里躺着一半 60 万一半 10 万,以后想查"现在到底几万"就只能逐行看。
    /// </remarks>
    public virtual bool NeedsRehash(string hashedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword)) return false;   // 空哈希轮不到重算,该走的是设初始密码那条路
        var parts = hashedPassword.Split('.');
        if (parts.Length != 4 || parts[0] != ALGORITHM) return true;
        return !int.TryParse(parts[1], out var iterations) || iterations != Iterations;
    }
}
