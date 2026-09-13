namespace SmartAdmin.Core;

/// <summary>
/// 上传配置(对应 <c>SmartAdmin:Upload</c> 节)。
/// </summary>
public class AdminUploadOptions
{
    /// <summary>提供者:<c>Local</c>(默认,本地磁盘)| OSS/Minio 等走 <c>IFileStorage</c> 扩展点(可选包)</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// 签名直链的有效分钟数;<b>默认 0 = 不过期</b>(链接会被存进公告正文这类持久内容,给寿命会让它们集体坏掉)。
    /// <para>只把直链用在头像、临时预览这类不入正文的场景时才配它。<b>一旦配上,此前签发的无期限链接立即失效</b>
    /// ——包括已存进库里的头像 URL;头像会在下次保存时重新签发,正文里的图片则要自己迁移。</para>
    /// </summary>
    public int SignedUrlTtlMinutes { get; set; }

    /// <summary>
    /// 秒传是否跨用户命中。<b>默认关</b>:开着的话,任何知道文件内容哈希的人都能凭"秒传"直接拿到一份引用,
    /// 而内容可枚举的文件(模板化 PDF、证件扫描件)算得出哈希就等于拿到了文件。
    /// <para>关闭后去重只在同一上传者内生效,代价是不同用户传同一份文件会各存一份。</para>
    /// </summary>
    public bool CrossUserDedupe { get; set; }

    /// <summary>
    /// 本地存储根目录。相对路径<b>按 ContentRoot 解析</b>(与 SQLite 库文件、JWT 开发密钥同一基准,不随进程 CWD 漂移);
    /// 绝对路径原样使用。正式部署声明为数据卷;<b>用 <c>UseStaticFiles()</c> 托管前端产物时必须挪出 <c>wwwroot</c></b>,
    /// 否则上传物会被静态中间件匿名直出,绕过鉴权下载(见 docs/deployment.md)。
    /// </summary>
    public string RootPath { get; set; } = "./wwwroot/upload";

    /// <summary>单文件大小上限(MB);超过拒收(<see cref="ErrorCode.FileTooLarge"/>)</summary>
    public int MaxSizeMb { get; set; } = 20;

    /// <summary>
    /// 允许的文件后缀白名单(含点、小写)。<b>按后缀而非 Content-Type 判定</b>,不以 Content-Type 为唯一依据。
    /// 空数组表示不限(不建议)。
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".xlsx", ".docx", ".zip"];

    /// <summary>是否启用后台磁盘回收。关掉 = 删了的文件永远占着盘,只在你另有回收手段时才关。</summary>
    public bool EnableGc { get; set; } = true;

    /// <summary>回收任务的执行间隔(小时)。它是运维参数,不进配置中心——改它值得重启一次。</summary>
    public int GcIntervalHours { get; set; } = 6;

    /// <summary>
    /// 软删文件的保留期(天):删除满这么久才真正删盘并抹掉记录。
    /// <para><b>不要把它调成 0。</b>「秒传」按内容哈希复用<b>同一条</b> <c>sys_file</c> 记录,
    /// 于是同一个文件 Id 可能被多个用户、多条业务记录引用——甲删掉"他的"文件,乙那边的引用也就跟着悬空了。
    /// 内核里没有文件引用表(那是消费者侧的契约),引用计数无从算起,保留期是这里唯一的安全网:
    /// 给人留出"删错了"的反应时间。</para>
    /// </summary>
    public int GcRetentionDays { get; set; } = 7;

    /// <summary>
    /// 分片上传会话的存活期(小时):这么久没有任何分片写入,视为客户端弃单(关页面/断网/取消),整个分片目录清掉。
    /// 续传中的会话每收一片就刷新,不受影响。
    /// </summary>
    public int GcChunkTtlHours { get; set; } = 24;
}
