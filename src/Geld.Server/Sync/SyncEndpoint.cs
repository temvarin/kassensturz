using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Domain.Sync;
using Geld.Domain.Contracts;

namespace Geld.Server.Sync;

/// <summary>
/// <c>POST /api/sync</c> applies a phone's changes to the shared pot and the member's own budget and answers with both.
/// The order in which requests arrive decides which of two changes to the same item wins.
/// </summary>
public static class SyncEndpoint
{
    /// <summary>How often to try again when another phone saved the document in the meantime.</summary>
    private const int Attempts = 3;

    public static async Task<IResult> Handle(HttpRequest http, ClaimsPrincipal user, IDocumentStore documents, CancellationToken cancellationToken)
    {
        // Against cross-site request forgery: the browser sends the sign-in cookie even with a form another site posts
        // here. A form can't send JSON, and another site's script may only send it if this server allows CORS, which it
        // never does.
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

        // The data belongs to the account the app last saw signed in. If the cookie now belongs to someone else, the
        // data must not end up in their budget.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (json.UserId != userId) return Results.StatusCode(StatusCodes.Status412PreconditionFailed);

        // The whole request is checked before the first document is saved. Applying it to empty documents runs every rule.
        SyncRequest request;
        try
        {
            request = json.ToDomain();
            KindergeldSync.Apply(new KindergeldPot(), request.Kindergeld);
            BudgetSync.Apply(new Budget(), request.Budget);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(); // the app itself never sends these
        }

        // The budget's id comes from the sign-in, never from the request, so nobody can touch another's.
        var kindergeld = await SaveAsync(documents, "kindergeld", request.Kindergeld.IsEmpty,
            ReadPot, pot => KindergeldSync.Apply(pot, request.Kindergeld), WritePot, cancellationToken);
        var budget = await SaveAsync(documents, $"budget:{userId}", request.Budget.IsEmpty,
            ReadBudget, b => BudgetSync.Apply(b, request.Budget), WriteBudget, cancellationToken);
        return kindergeld is null || budget is null
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) // the app tries again later
            : Results.Json(SyncResponseJson.From(new SyncResponse(kindergeld, budget)), ApiJson.Default.SyncResponseJson);
    }

    /// <summary>Applies the changes to the latest version of the document and saves it.</summary>
    /// <param name="read">Gets null if there is no row yet.</param>
    /// <returns>null if other requests kept saving first.</returns>
    private static async Task<T?> SaveAsync<T>(IDocumentStore documents, string id, bool unchanged, Func<string?, T> read,
        Func<T, T> apply, Func<T, string> write, CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var stored = await documents.GetAsync(id, cancellationToken);
            var document = read(stored?.Json);
            if (unchanged) return document; // no new version, nothing in the history
            var changed = apply(document);
            if (await documents.TrySaveAsync(id, write(changed), stored?.Version ?? 0, cancellationToken))
                return changed;
        }
        return null;
    }

    private static KindergeldPot ReadPot(string? row) =>
        row is null ? new() : Deserialize(row, DocumentJson.Default.KindergeldJson).ToDomain();

    private static string WritePot(KindergeldPot pot) =>
        JsonSerializer.Serialize(KindergeldJson.From(pot), DocumentJson.Default.KindergeldJson);

    private static Budget ReadBudget(string? row) =>
        row is null ? new() : Deserialize(row, DocumentJson.Default.BudgetJson).ToDomain();

    private static string WriteBudget(Budget budget) =>
        JsonSerializer.Serialize(BudgetJson.From(budget), DocumentJson.Default.BudgetJson);

    private static T Deserialize<T>(string row, JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(row, type) ?? throw new JsonException($"The stored document is null: {type.Type.Name}.");
}
