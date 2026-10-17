using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Geld.Domain;

namespace Geld.Server;

/// <summary>
/// <c>POST /api/sync</c>: applies a phone's changes to the shared pot and to the member's own budget, and answers with
/// both as they are then. The order in which requests arrive here decides which of two changes to the same item wins.
/// </summary>
public static class SyncEndpoint
{
    /// <summary>How often a document is read and changed again when another phone saved it in the meantime.</summary>
    private const int Attempts = 3;

    public static async Task<IResult> Handle(HttpRequest http, ClaimsPrincipal user, IDocumentStore documents, CancellationToken cancellationToken)
    {
        // The browser sends the sign-in cookie with every request to this site, even with a form that another website
        // posts here (cross-site request forgery). A form can't send JSON, and a script on another website may only
        // send it after asking this server, which never agrees (no CORS).
        if (!http.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        SyncRequest? request;
        try
        {
            request = await http.ReadFromJsonAsync(ApiJson.Default.SyncRequest, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (request is null) return Results.BadRequest();
        if (request.Protocol != SyncRequest.CurrentProtocol) return Results.Conflict(); // the app asks the user to reload

        try
        {
            // The budget's id comes from the sign-in, never from the request: nobody can read or write another's.
            var budgetId = $"budget:{user.FindFirstValue(ClaimTypes.NameIdentifier)}";
            var kindergeld = await SaveAsync(documents, "kindergeld", request.Kindergeld.IsEmpty,
                pot => pot.Apply(request.Kindergeld), ApiJson.Default.KindergeldPot, cancellationToken);
            var budget = await SaveAsync(documents, budgetId, request.Budget.IsEmpty,
                b => b.Apply(request.Budget), ApiJson.Default.Budget, cancellationToken);
            return kindergeld is null || budget is null
                ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) // the app tries again later
                : Results.Json(new SyncResponse(kindergeld, budget), ApiJson.Default.SyncResponse);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(); // changes that break the rules, which the app itself doesn't send
        }
    }

    /// <summary>Applies the changes to the latest version of the document (a new one if there is none yet) and saves it.</summary>
    /// <returns>The document as saved; null if other requests kept saving it first.</returns>
    private static async Task<T?> SaveAsync<T>(IDocumentStore documents, string id, bool unchanged, Action<T> apply,
        JsonTypeInfo<T> json, CancellationToken cancellationToken) where T : class, new()
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var stored = await documents.GetAsync(id, cancellationToken);
            var document = stored is null ? new T() : JsonSerializer.Deserialize(stored.Json, json)!;
            if (unchanged) return document; // nothing to save: no new version, nothing in the history
            apply(document);
            if (await documents.TrySaveAsync(id, JsonSerializer.Serialize(document, json), stored?.Version ?? 0, cancellationToken))
                return document;
        }
        return null;
    }
}
