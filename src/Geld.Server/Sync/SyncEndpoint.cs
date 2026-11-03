using System.Security.Claims;
using System.Text.Json;
using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Domain.Sync;
using Geld.Domain.Contracts;

namespace Geld.Server.Sync;

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
        SyncRequestJson? json;
        try
        {
            json = await http.ReadFromJsonAsync(ApiJson.Default.SyncRequestJson, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (json is null) return Results.BadRequest();
        if (json.Protocol != SyncRequestJson.CurrentProtocol) return Results.Conflict(); // the app asks the user to reload

        // The data the app sends belongs to the account it last saw signed in. If the cookie now belongs to someone
        // else, that data must not end up in their budget.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (json.UserId != userId) return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

        // All or nothing: the whole request is checked before the first document is saved. Applying the changes to
        // empty documents runs every rule.
        SyncRequest request;
        try
        {
            request = json.ToDomain();
            KindergeldSync.Apply(new(), request.Kindergeld);
            BudgetSync.Apply(new(), request.Budget);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(); // changes that break the rules, which the app itself doesn't send
        }

        // The budget's id comes from the sign-in, never from the request: nobody can read or write another's.
        var kindergeld = await SaveAsync(documents, "kindergeld", request.Kindergeld.IsEmpty,
            stored => stored is null ? new() : Read(stored, DocumentJson.Default.KindergeldJson).ToDomain(),
            pot => KindergeldSync.Apply(pot, request.Kindergeld),
            pot => JsonSerializer.Serialize(KindergeldJson.From(pot), DocumentJson.Default.KindergeldJson), cancellationToken);
        var budget = await SaveAsync(documents, $"budget:{userId}", request.Budget.IsEmpty,
            stored => stored is null ? new() : Read(stored, DocumentJson.Default.BudgetJson).ToDomain(),
            b => BudgetSync.Apply(b, request.Budget),
            b => JsonSerializer.Serialize(BudgetJson.From(b), DocumentJson.Default.BudgetJson), cancellationToken);
        return kindergeld is null || budget is null
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) // the app tries again later
            : Results.Json(SyncResponseJson.From(new SyncResponse(kindergeld, budget)), ApiJson.Default.SyncResponseJson);
    }

    /// <summary>Applies the changes to the latest version of the document (a new one if there is none yet) and saves it.</summary>
    /// <param name="read">The document from its stored JSON; null: there is none yet.</param>
    /// <returns>The document as saved; null if other requests kept saving it first.</returns>
    private static async Task<T?> SaveAsync<T>(IDocumentStore documents, string id, bool unchanged, Func<string?, T> read,
        Func<T, T> apply, Func<T, string> write, CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var stored = await documents.GetAsync(id, cancellationToken);
            var document = read(stored?.Json);
            if (unchanged) return document; // nothing to save: no new version, nothing in the history
            var changed = apply(document);
            if (await documents.TrySaveAsync(id, write(changed), stored?.Version ?? 0, cancellationToken))
                return changed;
        }
        return null;
    }

    private static T Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(json, type) ?? throw new JsonException($"The stored document is null: {type.Type.Name}.");
}
