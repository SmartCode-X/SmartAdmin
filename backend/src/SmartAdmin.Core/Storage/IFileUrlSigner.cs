namespace SmartAdmin.Core;

/// <summary>
/// 文件直链签名。给一个文件 Id 签出<b>不可伪造但可匿名访问</b>的 URL,让 <c>&lt;img src&gt;</c> 这类
/// 无法携带 Authorization 头的场景也能取到受管文件(通知公告正文里的图片是第一个用例)。
/// <para><b>这是一条能力链接(capability URL)</b>:拿得到链接就拿得到文件,拿不到就猜不出来(256 位 HMAC)。
/// 与 S3 presigned URL / 各家图床同一模型。</para>
/// <para><b>默认不带过期时间</b>,因为链接会被存进 Markdown 正文这类<b>持久内容</b>里,一条 30 分钟后失效的 URL
/// 等于"发布半小时后所有图片一起坏"。只把链接用在头像、临时预览这类不入正文的场景时,配
/// <c>SmartAdmin:Upload:SignedUrlTtlMinutes</c> 给它一个寿命——过期时间编进签名,改不了也伪造不了。
/// 想要"既进正文又有寿命",正文只存文件 Id、渲染时现签,那是消费者侧的事。</para>
/// <para>撤销手段:删掉文件(记录一软删,直链即 404),或轮换 <c>Jwt:SecretKey</c>(全部直链一起失效);
/// 配了 TTL 之后还多一条——等它自己过期。</para>
/// </summary>
public interface IFileUrlSigner
{
    /// <summary>签出某文件的签名(base64url,无填充)。</summary>
    string Sign(long fileId);

    /// <summary>校验签名是否匹配该文件 Id(定长时间比较,不泄漏前缀信息)。空/畸形签名一律 false。</summary>
    bool Verify(long fileId, string? signature);

    /// <summary>拼出可直接塞进 <c>&lt;img src&gt;</c> 的相对 URL(含签名;配了 TTL 则含 <c>exp</c>)。</summary>
    string BuildUrl(long fileId);

    /// <summary>
    /// 签出带过期时刻的签名;<paramref name="expiresAt"/> 为 null 即永久链接。
    /// <para>默认实现忽略过期参数、回落 <see cref="Sign(long)"/> —— 既有第三方实现无需改动即可编译,行为不变。</para>
    /// </summary>
    string Sign(long fileId, DateTimeOffset? expiresAt) => Sign(fileId);

    /// <summary>
    /// 校验带过期时刻的签名:签名须匹配<b>同一个</b> <paramref name="expiresAt"/>(过期时刻编进签名,改了就验不过),
    /// 且当前时刻未超过它。
    /// <para>默认实现忽略过期参数、回落 <see cref="Verify(long, string?)"/>。</para>
    /// </summary>
    bool Verify(long fileId, string? signature, DateTimeOffset? expiresAt) => Verify(fileId, signature);
}
