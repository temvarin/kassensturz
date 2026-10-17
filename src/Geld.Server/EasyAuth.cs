using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Geld.Server;

/// <summary>
/// Authentication, "who is calling?". The sign-in itself is done by App Service's Easy Auth, which runs in front of this
/// app: it signs the user in with Microsoft, keeps the session, and passes the user on in request headers. It removes
/// these headers from requests coming from outside, so they can only come from Easy Auth.
/// </summary>
public sealed class EasyAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "EasyAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].ToString();
        // Without Easy Auth nobody removes the headers and anyone could send them. App Service sets this variable
        // while Easy Auth is on.
        var easyAuthOn = string.Equals(configuration["WEBSITE_AUTH_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (id.Length == 0 || !easyAuthOn) return Task.FromResult(AuthenticateResult.NoResult());

        Claim[] claims = [new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].ToString())];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Name)));
    }
}
