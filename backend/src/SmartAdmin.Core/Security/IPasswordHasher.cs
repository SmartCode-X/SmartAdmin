namespace SmartAdmin.Core;

/// <summary>
/// 密码哈希器(扩展点)。
/// <para>默认实现为 PBKDF2(BCL <c>Rfc2898DeriveBytes</c>,零第三方依赖)。
/// 存储格式要求<b>自描述</b>:算法版本、迭代次数、盐、哈希一并编码进产出字符串,
/// 使未来升级算法/加大迭代时旧密码仍可校验、并可在用户下次登录时无感重哈希。</para>
/// <para>典型替换场景:对接已有用户库(沿用其哈希算法)、接入硬件加密机。
/// 在 <c>AddSmartAdmin()</c> 之前注册自己的实现即可生效。</para>
/// </summary>
public interface IPasswordHasher
{
    /// <summary>对明文密码生成自描述哈希串(每次调用产生新随机盐,同一明文两次结果不同)</summary>
    string Hash(string password);

    /// <summary>校验明文与哈希串是否匹配。实现必须使用恒定时间比较,防时序侧信道。</summary>
    bool Verify(string password, string hashedPassword);

    /// <summary>
    /// 这个哈希串是否该按当前参数重算。校验通过后调用方据此无感升级(用户不必改密)。
    /// <para>默认 <c>false</c> = 从不重算,第三方实现不受影响。</para>
    /// </summary>
    bool NeedsRehash(string hashedPassword) => false;
}
