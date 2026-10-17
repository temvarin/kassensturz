using System.Security.Claims;
using Geld.Domain;
using Geld.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

const string Member = "Member";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(EasyAuth.Name).AddScheme<AuthenticationSchemeOptions, EasyAuth>(EasyAuth.Name, null);
// Authorization, "may this user use the family's data?": only the user ids in the app setting Geld__Members
// (comma-separated). Not signed in: 401; signed in but not listed: 403.
builder.Services.AddAuthorization();
builder.Services.AddOptions<AuthorizationOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var members = (configuration["Geld:Members"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    options.AddPolicy(Member, policy => policy.RequireAssertion(context =>
        members.Contains(context.User.FindFirstValue(ClaimTypes.NameIdentifier))));
});
var app = builder.Build();

// The Blazor app's files, served with their pre-compressed variants and caching headers.
app.MapStaticAssets();

// Signed in is enough here: this is how a new user learns the id to put on the members list.
app.MapGet("/api/whoami", async (ClaimsPrincipal user, IAuthorizationService authorization) => new WhoAmI(
        user.FindFirstValue(ClaimTypes.NameIdentifier)!,
        user.Identity?.Name ?? "",
        (await authorization.AuthorizeAsync(user, Member)).Succeeded))
    .RequireAuthorization();
// Everything else under /api is for members only, including endpoints added later: Easy Auth lets everyone through
// to this app (which must load without signing in), so this is the gate.
var api = app.MapGroup("/api").RequireAuthorization(Member);
// Unknown API routes are errors, not app pages.
api.Map("{**path}", () => Results.NotFound());

// Routes like /budget exist only inside the app: answer them with its start page, which must never be cached
// so that a new release is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
