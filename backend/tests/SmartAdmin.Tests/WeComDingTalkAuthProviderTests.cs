using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAdmin.Auth.DingTalk;
using SmartAdmin.Auth.WeCom;
using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>企微 / 钉钉可选包:假 HttpMessageHandler 覆盖 happy path 与异常映射(对齐 GitHub 加固,不触网)。</summary>
public class WeComDingTalkAuthProviderTests
{
    private sealed class SeqHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string json) =>
            _steps.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }));

        public void EnqueueThrow(Exception ex) =>
            _steps.Enqueue((_, _) => throw ex);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_steps.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return _steps.Dequeue()(request, cancellationToken);
        }
    }

    private static ExternalExchangeRequest Ex(string code = "auth-code") =>
        new(code, "verifier", "https://app/cb", "nonce");

    private static WeComExternalAuthProvider WeCom(SeqHandler? h = null) => new(
        new WeComAuthOptions { CorpId = "wwcorp", AgentId = "1000002", CorpSecret = "s" },
        new HttpClient(h ?? new SeqHandler()),
        NullLogger<WeComExternalAuthProvider>.Instance);

    [Fact]
    public async Task WeCom_authorize_url_outside_the_wecom_client_is_the_qr_login_page()
    {
        var url = await WeCom().BuildAuthorizeUrlAsync(
            new ExternalAuthorizeRequest("st", "n", "ch", "https://admin.example.com/cb"));

        Assert.StartsWith("https://login.work.weixin.qq.com/wwlogin/sso/login?login_type=CorpApp", url);
        Assert.Contains("&appid=wwcorp", url);
        Assert.Contains("&agentid=1000002", url);
        Assert.Contains("&redirect_uri=https%3A%2F%2Fadmin.example.com%2Fcb", url);
        Assert.Contains("&state=st", url);
    }

    // 企业微信 FAQ 给的客户端 UA:手机端与桌面端都带 wxwork,也都带微信的 MicroMessenger
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/14F89 wxwork/4.1.20 MicroMessenger/7.0.1 Language/zh")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) wxwork/4.1.20 (MicroMessenger/6.2) WindowsWechat")]
    public async Task WeCom_authorize_url_inside_the_wecom_client_is_silent_oauth(string userAgent)
    {
        var url = await WeCom().BuildAuthorizeUrlAsync(
            new ExternalAuthorizeRequest("st", "n", "ch", "https://admin.example.com/cb") { UserAgent = userAgent });

        Assert.Equal(
            "https://open.weixin.qq.com/connect/oauth2/authorize?appid=wwcorp" +
            "&redirect_uri=https%3A%2F%2Fadmin.example.com%2Fcb&response_type=code&scope=snsapi_base" +
            "&state=st&agentid=1000002#wechat_redirect",
            url);
    }

    [Fact]
    public async Task WeCom_authorize_url_in_the_personal_wechat_client_stays_qr_login()
    {
        // 个人微信的 UA 只有 MicroMessenger 没有 wxwork:企业微信的网页授权在那里用不了
        var url = await WeCom().BuildAuthorizeUrlAsync(new ExternalAuthorizeRequest("st", "n", "ch", "https://cb")
        {
            UserAgent = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Mobile Safari/537.36 MicroMessenger/8.0.49",
        });

        Assert.StartsWith("https://login.work.weixin.qq.com/wwlogin/sso/login?", url);
    }

    [Fact]
    public async Task WeCom_exchange_maps_userid()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"access_token":"tok","expires_in":7200}""");
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"userid":"zhangsan"}""");
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("wecom", id.Provider);
        Assert.Equal("zhangsan", id.Subject);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task WeCom_http_failure_maps_to_oauth_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("net"));
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task WeCom_missing_userid_fails()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0,"access_token":"tok","expires_in":7200}""");
        h.Enqueue(HttpStatusCode.OK, """{"errcode":0}""");
        var p = new WeComExternalAuthProvider(
            new WeComAuthOptions { CorpId = "c", AgentId = "1", CorpSecret = "s" },
            new HttpClient(h),
            NullLogger<WeComExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task DingTalk_exchange_maps_unionId_and_nick()
    {
        var h = new SeqHandler();
        h.Enqueue(HttpStatusCode.OK, """{"accessToken":"utok"}""");
        h.Enqueue(HttpStatusCode.OK, """{"unionId":"u1","nick":"钉钉用户","email":"a@b.c"}""");
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "k", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<DingTalkExternalAuthProvider>.Instance);

        var id = await p.ExchangeAsync(Ex());
        Assert.Equal("dingtalk", id.Provider);
        Assert.Equal("u1", id.Subject);
        Assert.Equal("钉钉用户", id.DisplayName);
        Assert.Equal("a@b.c", id.Email);
        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("x-acs-dingtalk-access-token", h.Requests[1].Headers.Select(x => x.Key));
    }

    [Fact]
    public async Task DingTalk_http_failure_maps_to_oauth_exchange_failed()
    {
        var h = new SeqHandler();
        h.EnqueueThrow(new HttpRequestException("net"));
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "k", AppSecret = "s" },
            new HttpClient(h),
            NullLogger<DingTalkExternalAuthProvider>.Instance);

        var ex = await Assert.ThrowsAsync<AdminException>(() => p.ExchangeAsync(Ex()));
        Assert.Equal(ErrorCode.OAuthExchangeFailed, ex.Code);
    }

    [Fact]
    public async Task DingTalk_authorize_url_contains_client_and_state()
    {
        var p = new DingTalkExternalAuthProvider(
            new DingTalkAuthOptions { AppKey = "appk", AppSecret = "s" },
            new HttpClient(new SeqHandler()),
            NullLogger<DingTalkExternalAuthProvider>.Instance);
        var url = await p.BuildAuthorizeUrlAsync(new ExternalAuthorizeRequest("st", "n", "ch", "https://cb"));
        Assert.Contains("client_id=appk", url);
        Assert.Contains("state=st", url);
        Assert.Contains("redirect_uri=", url);
    }
}
