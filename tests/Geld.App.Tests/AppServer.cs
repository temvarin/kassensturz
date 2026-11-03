using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Geld.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

/// <summary>Someone Easy Auth signs in. Whether they are a member is the server's setting, as in production.</summary>
public sealed record TestUser(string Id, string Name);

/// <summary>
/// The real Geld.Server, running in memory: its sync endpoint, sign-in and members list, with the documents in memory
/// (MemoryDocumentStore, which passes the SQL store's tests). In front of it, the network, with what a test lets go
/// wrong there: an answer that never comes back, a slow line, an error from elsewhere. The server starts with the first
/// request, so tests that never sync don't pay for it.
/// </summary>
public sealed class AppServer : IDisposable
{
    public static TestUser Anna { get; } = new("8a1c0e52", "anna@example.com");
    public static TestUser Ben { get; } = new("3f9d2b77", "ben@example.com");

    /// <summary>Signed in with Microsoft, but not on the members list.</summary>
    public static TestUser Stranger { get; } = new("5c7e1a90", "stranger@example.com");

    private static readonly Uri Address = new("http://localhost/");

    private readonly MemoryDocumentStore documents = new();
    private readonly Lazy<WebApplicationFactory<Geld.Server.Account.EasyAuth>> host;
    private readonly Lazy<HttpMessageInvoker> server;

    public AppServer()
    {
        // Any type of Geld.Server names its assembly; the factory starts that assembly's Program.
        host = new(() => new WebApplicationFactory<Geld.Server.Account.EasyAuth>().WithWebHostBuilder(web =>
        {
            web.UseSetting("WEBSITE_AUTH_ENABLED", "True"); // the sign-in headers count only behind Easy Auth
            web.UseSetting("Geld:Members", $"{Anna.Id},{Ben.Id}");
            web.ConfigureTestServices(services => services.AddSingleton<Geld.Server.Sync.IDocumentStore>(documents));
        }));
        server = new(() => new HttpMessageInvoker(host.Value.Server.CreateHandler()));
        Network = new NetworkHandler(this);
    }

    /// <summary>Who is signed in, as the app's requests arrive; null: nobody.</summary>
    public TestUser? User { get; set; }

    /// <summary>The app's way to the server.</summary>
    public HttpMessageHandler Network { get; }

    /// <summary>The sync requests the app sent, including the ones that got an error.</summary>
    public List<SyncRequest> Syncs { get; } = [];

    /// <summary>Answers for the next sync requests, instead of the server's: errors from elsewhere on the way.</summary>
    public Queue<Func<HttpResponseMessage>> Next { get; } = [];

    /// <summary>The next sync reaches the server and is saved, but its answer gets lost on the way back.</summary>
    public bool LoseNextAnswer { get; set; }

    /// <summary>While set, sync requests wait until it completes: a slow network.</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Instead of the server, e.g. no network at all, or the development server, which has no API.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }

    /// <summary>The shared pot as the server has stored it.</summary>
    public KindergeldPot Kindergeld => Stored("kindergeld", DocumentJson.Default.KindergeldJson)?.ToDomain() ?? new();

    /// <summary>A member's budget as the server has stored it; null if there is none.</summary>
    public Budget? BudgetOf(TestUser user) => Stored($"budget:{user.Id}", DocumentJson.Default.BudgetJson)?.ToDomain();

    /// <summary>A change made on another phone and synced from there, as <paramref name="user"/>.</summary>
    /// <returns>What <paramref name="change"/> returned, e.g. the new entry.</returns>
    public async Task<T> OnOtherPhone<T>(TestUser user, Func<AppData, T> change)
    {
        var phone = new AppData();
        var result = change(phone);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Address, "api/sync"))
        {
            Content = JsonContent.Create(SyncRequestJson.From(phone.Synced.StartSync(user.Id).Request), ApiJson.Default.SyncRequestJson),
        };
        using var response = await Send(request, user, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        return result;
    }

    public void Dispose()
    {
        if (server.IsValueCreated) server.Value.Dispose();
        if (host.IsValueCreated) host.Value.Dispose();
    }

    // Easy Auth signs the user in and passes them on in these headers.
    private Task<HttpResponseMessage> Send(HttpRequestMessage request, TestUser? user, CancellationToken cancellationToken)
    {
        if (user is not null)
        {
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user.Id);
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", user.Name);
        }
        return server.Value.SendAsync(request, cancellationToken);
    }

    private T? Stored<T>(string id, JsonTypeInfo<T> type) where T : class =>
        documents.GetAsync(id).GetAwaiter().GetResult() is { } document ? JsonSerializer.Deserialize(document.Json, type) : null;

    /// <summary>The network between the app and the server: what a test lets go wrong there, and when the app goes on.</summary>
    private sealed class NetworkHandler(AppServer owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (owner.Override is { } other) return other(request);
            if (request.RequestUri!.AbsolutePath == "/api/sync")
            {
                await request.Content!.LoadIntoBufferAsync(cancellationToken); // read here, and again by the server
                owner.Syncs.Add((await request.Content.ReadFromJsonAsync(ApiJson.Default.SyncRequestJson, cancellationToken))!.ToDomain());
                if (owner.Hold is { } hold) await hold.Task;
                if (owner.Next.TryDequeue(out var answer)) return answer();
            }
            // The app goes on only once the server has answered: in the browser the app runs on one thread, and in a
            // test, answers coming back on the thread pool would let the app interleave with the test by chance. When
            // things happen in between is the test's decision (Hold). The server runs on its own threads meanwhile.
            var response = Task.Run(() => owner.Send(request, owner.User, cancellationToken), cancellationToken).GetAwaiter().GetResult();
            if (request.RequestUri.AbsolutePath == "/api/sync" && owner.LoseNextAnswer)
            {
                owner.LoseNextAnswer = false;
                response.Dispose();
                throw new HttpRequestException("The connection was lost.");
            }
            return response;
        }
    }
}
