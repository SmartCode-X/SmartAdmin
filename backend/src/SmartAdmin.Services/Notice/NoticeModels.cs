using SmartAdmin.Core;

namespace SmartAdmin.Services;

/// <summary>发布通知入参(管理员)。可发给全体 / 指定角色 / 指定用户。</summary>
public record NoticePublishInput
{
    /// <summary>标题</summary>
    public string Title { get; init; } = "";

    /// <summary>正文</summary>
    public string? Content { get; init; }

    /// <summary>类型(通知 / 公告 / 消息)</summary>
    public NoticeType Type { get; init; } = NoticeType.Notice;

    /// <summary>接收范围(全体 / 角色 / 用户)</summary>
    public ReceiverType ReceiverType { get; init; } = ReceiverType.All;

    /// <summary>接收目标 Id 集合(角色 Id 或用户 Id,取决于 <see cref="ReceiverType"/>);为 <see cref="ReceiverType.All"/> 时忽略。</summary>
    public IReadOnlyCollection<long>? ReceiverIds { get; init; }

    /// <summary>动作列表(可选,不传即无按钮);形状与限制见 <see cref="NoticeAction"/>。</summary>
    public IReadOnlyList<NoticeAction>? Actions { get; init; }
}

/// <summary>
/// 通知上的一个动作:前端渲染成正文下的按钮,点击后按 <see cref="Method"/> 请求 <see cref="Url"/>(接口)
/// 或跳转到该站内页面。通知是信封不是工作流——服务端只存不执行,权限仍由目标端点自己判。
/// <para><see cref="Url"/> 只接受以<b>单个</b> <c>/</c> 开头的站内相对路径:<c>//evil.com</c> 与 <c>/\evil.com</c>
/// 是协议相对 URL,发布时即拒绝(<see cref="ErrorCode.NoticeActionInvalid"/>)。</para>
/// </summary>
public record NoticeAction
{
    /// <summary>按钮文案;含 <c>.</c> 视为 i18n key(与菜单标题同一条约定)</summary>
    public string Label { get; init; } = "";

    /// <summary>请求方法:GET 或 POST。url 指向站内页面(非 /api/)时前端忽略它,直接路由跳转</summary>
    public string Method { get; init; } = "GET";

    /// <summary>站内相对路径:<c>/api/v1/…</c> 是接口调用,其它 <c>/…</c> 是页面跳转</summary>
    public string Url { get; init; } = "";

    /// <summary>按钮样式:primary / default / error;缺省 default</summary>
    public string? Style { get; init; }

    /// <summary>点击前先弹确认框</summary>
    public bool Confirm { get; init; }

    /// <summary>点击前先弹意见输入框,把 <c>{ comment }</c> 作为请求体带上</summary>
    public bool Comment { get; init; }
}

/// <summary>通知分页查询入参(管理端全量列表 / 用户端"我的通知"共用)。</summary>
public record NoticePageInput : PageInputBase
{
    /// <summary>标题(模糊匹配,可选)</summary>
    public string? Title { get; init; }

    /// <summary>类型(精确匹配,可选)</summary>
    public NoticeType? Type { get; init; }

    /// <summary>仅未读(用户端"未读"页签用;管理端列表忽略)</summary>
    public bool? OnlyUnread { get; init; }
}

/// <summary>"我的通知"列表项:通知内容 + 当前用户是否已读。</summary>
public record NoticeMineItem
{
    /// <summary>通知 Id</summary>
    public long Id { get; init; }

    /// <summary>标题</summary>
    public string Title { get; init; } = "";

    /// <summary>正文</summary>
    public string? Content { get; init; }

    /// <summary>类型</summary>
    public NoticeType Type { get; init; }

    /// <summary>发布时间(= 通知创建时间)</summary>
    public DateTime PublishTime { get; init; }

    /// <summary>当前用户是否已读</summary>
    public bool IsRead { get; init; }

    /// <summary>动作列表(发布时带的原样透传;没有即 null)</summary>
    public IReadOnlyList<NoticeAction>? Actions { get; init; }
}
