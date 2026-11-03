using System.Security.Claims;
using Geld.Domain.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Geld.Server.Account;

/// <summary>Who is calling (<see cref="EasyAuth"/>), and whether they may use the family's data.</summary>
public static class Members
{
    /// <summary>The authorization policy for the family's data.</summary>
    public const string Policy = "Member";

    public static IServiceCollection AddFamilySignIn(this IServiceCollection services)
    {
        services.AddAuthentication(EasyAuth.Name).AddScheme<AuthenticationSchemeOptions, EasyAuth>(EasyAuth.Name, null);
        // Authorization, "may this user use the family's data?": only the user ids in the app setting Geld__Members
        // (comma-separated). Not signed in: 401; signed in but not listed: 403.
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var members = (configuration["Geld:Members"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            options.AddPolicy(Policy, policy => policy.RequireAssertion(context =>
                members.Contains(context.User.FindFirstValue(ClaimTypes.NameIdentifier))));
        });
        return services;
    }

    /// <summary>
    /// <c>/api/whoami</c>. Signed in is enough here: this is how a new user learns the id to put on the members list. The
    /// app asks at every start, which is when the sign-in is made to outlast closing the app.
    /// </summary>
    public static RouteHandlerBuilder MapWhoAmI(this IEndpointRouteBuilder app) => app.MapGet("/api/whoami",
        async (HttpContext context, IAuthorizationService authorization, TimeProvider clock) =>
        {
            EasyAuth.KeepSession(context, clock);
            var user = context.User;
            return new WhoAmI(
                user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                user.Identity?.Name ?? "",
                (await authorization.AuthorizeAsync(user, Policy)).Succeeded);
        })
        .RequireAuthorization();
}
