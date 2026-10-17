using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Geld.Domain;

namespace Geld.App.Tests;

/// <summary>The server as the app sees it: Easy Auth's signed-in user, and the sync with the documents in memory.</summary>
public sealed class FakeServer
{
    /// <summary>null: nobody is signed in.</summary>
    public WhoAmI? User { get; set; }

    public KindergeldPot Kindergeld { get; } = new();
    public Dictionary<string, Budget> Budgets { get; } = [];

    /// <summary>The sync requests received, including the ones answered with an error.</summary>
    public List<SyncRequest> Syncs { get; } = [];

    /// <summary>Answers for the next sync requests, instead of syncing (e.g. an error).</summary>
    public Queue<Func<HttpResponseMessage>> Next { get; } = [];

    /// <summary>While set, sync requests wait until it completes: a slow network.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>For requests the app sends without the server's API, e.g. to the development server.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }

    public static WhoAmI Anna { get; } = new("8a1c0e52", "anna@example.com", true);
    public static WhoAmI Ben { get; } = new("3f9d2b77", "ben@example.com", true);

    public async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
    {
        if (Override is { } other) return other(request);
        if (User is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/api/whoami":
                return Json(JsonSerializer.Serialize(User, ApiJson.Default.WhoAmI));
            case "/api/sync":
                var sync = (await request.Content!.ReadFromJsonAsync(ApiJson.Default.SyncRequest))!;
                Syncs.Add(sync);
                if (Hold is { } hold) await hold.Task;
                if (Next.TryDequeue(out var answer)) return answer();
                if (!User.Member) return new HttpResponseMessage(HttpStatusCode.Forbidden);
                var budget = Budgets.TryGetValue(User.Id, out var b) ? b : Budgets[User.Id] = new Budget();
                Kindergeld.Apply(sync.Kindergeld);
                budget.Apply(sync.Budget);
                return Json(JsonSerializer.Serialize(new SyncResponse(Kindergeld, budget), ApiJson.Default.SyncResponse));
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
