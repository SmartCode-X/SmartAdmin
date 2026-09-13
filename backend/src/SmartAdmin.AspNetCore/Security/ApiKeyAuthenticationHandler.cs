using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>API Key 认证 scheme 的常量。</summary>
public static class ApiKeyDefaults
{
    /// <summary>scheme 名;<c>[ApiKey]</c> 即 <c>[Authorize(AuthenticationSchemes = "ApiKey")]</c>。</summary>
    public const string Scheme = "ApiKey";
}

/// <summary>
/// 机器端接入的认证 scheme:从请求头(默认 <c>X-Api-Key</c>,见 <see cref="AdminApiKeyOptions.HeaderName"/>)取 key,
/// 交 <see cref="IApiKeyValidator"/> 校验,通过即签发一个"像用户"的主体——<c>unique_name</c> 与 <c>akn</c> 是 key 的名字,
/// 绑了用户的 key 再带 <c>sub</c>,于是 <c>[RolePermission]</c>、数据范围、操作日志全部照旧复用,不另造一套授权模型。
/// <para>与 JWT 并存互不干扰:只有端点显式挂了 <c>[ApiKey]</c>(或 <c>AuthenticationSchemes</c> 点名本 scheme)才走到这里;
/// 没带头 = 无结果(叠 Bearer 的端点仍可用 JWT),带了错的 = 失败。401 / 403 都套统一信封。</para>
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AdminSecurityOptions security,
    IApiKeyValidator validator) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = security.ApiKey.HeaderName;
        if (!Request.Headers.TryGetValue(header, out var values)) return AuthenticateResult.NoResult();
        var presented = values.ToString().Trim();
        if (presented.Length == 0) return AuthenticateResult.NoResult();

        var principal = await validator.ValidateAsync(presented, Context.RequestAborted);
        if (principal is null) return AuthenticateResult.Fail("API key 无效");

        return AuthenticateResult.Success(new AuthenticationTicket(BuildPrincipal(principal), Scheme.Name));
    }

    /// <summary>key → ClaimsPrincipal。覆写可追加自己的 claim(如租户)。</summary>
    protected virtual ClaimsPrincipal BuildPrincipal(ApiKeyPrincipal principal)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.UniqueName, principal.Name),
            new(TokenClaimNames.API_KEY, principal.Name),
        };
        if (principal.UserId is long userId)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, JwtRegisteredClaimNames.UniqueName, roleType: null);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>401:key 缺失或无效(<see cref="ErrorCode.ApiKeyInvalid"/>),与 JWT 的 40006 分开,机器调用方一眼能分。</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.ApiKeyInvalid), Context.RequestAborted);
    }

    /// <summary>403:统一信封 41001,与 <c>[RolePermission]</c> 的出口一致。</summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(Result<object>.Fail(ErrorCode.NoPermission), Context.RequestAborted);
    }
}
