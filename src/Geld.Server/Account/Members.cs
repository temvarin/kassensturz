using System.Security.Claims;
using Geld.Domain.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Geld.Server.Account;

/// <summary>Whether the caller may use the family's data.</summary>
public static class Members
{
    public const string Policy = "Member";

    public static IServiceCollection AddFamilySignIn(this IServiceCollection services)
    {
        services.AddAuthentication(EasyAuth.Name).AddScheme<AuthenticationSchemeOptions, EasyAuth>(EasyAuth.Name, null);
        // Only the user ids in the app setting Geld__Members, comma-separated. Not signed in gives 401, not listed 403.
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
    /// <c>/api/whoami</c> needs only a sign-in, because that is how a new user learns the id for the members list. The
    /// app asks at every start, which is when the session cookie gets its expiry date.
    /// </summary>
    public static RouteHandlerBuilder MapWhoAmI(this IEndpointRouteBuilder app) => app.MapGet("/api/whoami",
        async (HttpContext context, IAuthorizationService authorization, TimeProvider clock) =>
        {
            EasyAuth.KeepSession(context, clock);
            var user = context.User;
            var me = new WhoAmI(
                user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                user.Identity?.Name ?? "",
                (await authorization.AuthorizeAsync(user, Policy)).Succeeded);
            return Results.Json(me, ApiJson.Default.WhoAmI);
        })
        .RequireAuthorization();
}
