using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Geld.Server.Account;

/// <summary>
/// Who is calling. App Service's Easy Auth runs in front of this app, signs the user in with Microsoft and passes the
/// user on in request headers, which it strips from requests coming from outside.
/// </summary>
/// <remarks>
/// A developer's machine has no Easy Auth. There, in the Development environment only, the setting <c>Geld:DevUser</c>
/// signs every request in as that user.
/// </remarks>
public sealed class EasyAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IConfiguration configuration, IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "EasyAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsDevelopment() && configuration["Geld:DevUser"] is { Length: > 0 } devUser)
            return Task.FromResult(SignedIn(devUser, "dev@localhost"));

        var id = Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].ToString();
        // Without Easy Auth nobody strips the headers, and anyone could send them. App Service sets this variable
        // while Easy Auth is on.
        var easyAuthOn = string.Equals(configuration["WEBSITE_AUTH_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (id.Length == 0 || !easyAuthOn) return Task.FromResult(AuthenticateResult.NoResult());

        return Task.FromResult(SignedIn(id, Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].ToString()));
    }

    /// <summary>Easy Auth's setting <c>login.cookieExpiration</c>, counted from the sign-in.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const string SessionCookie = "AppServiceAuthSession";

    /// <summary>
    /// Easy Auth's session cookie has no expiry date, so a phone drops it when the installed app is closed. This sends it
    /// back unchanged with an expiry date. Easy Auth still refuses a session older than <see cref="SessionLifetime"/>.
    /// </summary>
    public static void KeepSession(HttpContext context, TimeProvider clock)
    {
        // Read raw, because ASP.NET's cookie collections decode and encode values, which could change it.
        if (!CookieHeaderValue.TryParseList(context.Request.Headers.Cookie, out var cookies)) return;
        if (cookies.FirstOrDefault(cookie => cookie.Name == SessionCookie) is not { } session) return;
        context.Response.Headers.Append(HeaderNames.SetCookie, new SetCookieHeaderValue(SessionCookie, session.Value)
        {
            Path = "/",
            Expires = clock.GetUtcNow() + SessionLifetime,
            Secure = true,
            HttpOnly = true,
            SameSite = Microsoft.Net.Http.Headers.SameSiteMode.Lax,
        }.ToString());
    }

    private static AuthenticateResult SignedIn(string id, string name)
    {
        Claim[] claims = [new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, name)];
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Name)), Name));
    }
}
