using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Geld.Server;

/// <summary>
/// Authentication, "who is calling?". The sign-in itself is done by App Service's Easy Auth, which runs in front of this
/// app: it signs the user in with Microsoft, keeps the session, and passes the user on in request headers. It removes
/// these headers from requests coming from outside, so they can only come from Easy Auth.
/// </summary>
/// <remarks>
/// On a developer's machine there is no Easy Auth: there, the setting <c>Geld:DevUser</c> (launchSettings.json) signs
/// every request in as that user id. Only in the Development environment, which App Service never runs in.
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
        // Without Easy Auth nobody removes the headers and anyone could send them. App Service sets this variable
        // while Easy Auth is on.
        var easyAuthOn = string.Equals(configuration["WEBSITE_AUTH_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (id.Length == 0 || !easyAuthOn) return Task.FromResult(AuthenticateResult.NoResult());

        return Task.FromResult(SignedIn(id, Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].ToString()));
    }

    /// <summary>How long Easy Auth accepts a session: its setting <c>login.cookieExpiration</c>, fixed from the sign-in.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const string SessionCookie = "AppServiceAuthSession";

    /// <summary>
    /// Easy Auth gives its session cookie no expiry date, so the browser deletes it when its session ends: on a phone,
    /// when the installed app is closed. This sends the cookie back unchanged, with an expiry date, so that the phone
    /// keeps it for as long as Easy Auth accepts it. Easy Auth still decides: it refuses a session older than
    /// <see cref="SessionLifetime"/> and deletes the cookie then, as it does when signing out.
    /// </summary>
    public static void KeepSession(HttpContext context)
    {
        // The value as it came: ASP.NET's cookie collections decode and encode values, which could change it.
        if (!CookieHeaderValue.TryParseList(context.Request.Headers.Cookie, out var cookies)) return;
        if (cookies.FirstOrDefault(cookie => cookie.Name == SessionCookie) is not { } session) return;
        context.Response.Headers.Append(HeaderNames.SetCookie, new SetCookieHeaderValue(SessionCookie, session.Value)
        {
            Path = "/",
            Expires = DateTimeOffset.UtcNow + SessionLifetime,
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
