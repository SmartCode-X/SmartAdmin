namespace SmartAdmin.Auth.WeChat;

/// <summary>微信开放平台网站应用配置(<c>SmartAdmin:ExternalAuth:WeChat</c>)。Code 固定 <c>wechat</c>。</summary>
public class WeChatAuthOptions
{
    /// <summary>微信开放平台网站应用 AppId。</summary>
    public string AppId { get; set; } = "";

    /// <summary>微信开放平台网站应用 AppSecret。</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>空则回退「微信」。</summary>
    public string DisplayName { get; set; } = "微信";

    /// <summary>图标(iconify 名或图片 URL,可空)。</summary>
    public string? Icon { get; set; }
}
