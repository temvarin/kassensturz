using System.Net;
using System.Net.Http.Json;
using System.Text;
using Geld.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.Server.Tests;

/// <summary><c>POST /api/sync</c>, with the documents in memory (<see cref="DocumentStoreTests"/> covers the database).</summary>
public class SyncApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly MemoryDocuments documents = new();

    private HttpClient Server() => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("WEBSITE_AUTH_ENABLED", "True");
        host.UseSetting("Geld:Members", "anna,ben");
        host.ConfigureTestServices(services => services.AddSingleton<IDocumentStore>(documents));
    }).CreateClient();

    private static SyncRequest Changes(AppData phone) => phone.StartSync().Request;

    private async Task<HttpResponseMessage> Post(string user, HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sync", UriKind.Relative)) { Content = content };
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user);
        return await Server().SendAsync(request);
    }

    private Task<HttpResponseMessage> Post(string user, SyncRequest sync) => Post(user, JsonContent.Create(sync, ApiJson.Default.SyncRequest));

    private async Task<SyncResponse> Sync(string user, SyncRequest sync)
    {
        var response = await Post(user, sync);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync(ApiJson.Default.SyncResponse))!;
    }

    [Fact]
    public async Task The_pot_is_shared_and_each_budget_belongs_to_its_member()
    {
        var anna = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        anna.Budget.SetSalary(2850);
        await Sync("anna", Changes(anna));

        var ben = await Sync("ben", Changes(new AppData()));

        Assert.Equal([payment], ben.Kindergeld.Entries);
        Assert.Equal(0m, ben.Budget.NetSalary);
        Assert.Equal(["budget:anna", "kindergeld"], documents.Ids); // Ben's budget is saved once he enters something
    }

    [Fact]
    public async Task A_sync_without_changes_only_reads()
    {
        var anna = new AppData();
        anna.Kindergeld.Receive(Today, 518);
        await Sync("anna", Changes(anna));

        await Sync("anna", Changes(new AppData()));

        Assert.Equal(1, documents.Saves["kindergeld"]);
    }

    [Fact]
    public async Task When_another_phone_saves_first_the_change_is_applied_to_its_version()
    {
        var anna = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        var ben = new AppData();
        var jacket = ben.Kindergeld.Spend(Today, 49.99m, "Kleidung");
        documents.BeforeNextSave = async () => await documents.TrySaveAsync("kindergeld",
            System.Text.Json.JsonSerializer.Serialize(Apply(anna), ApiJson.Default.KindergeldPot), 0);

        var answer = await Sync("ben", Changes(ben));

        Assert.Equal([jacket, payment], answer.Kindergeld.Entries);
    }

    [Fact]
    public async Task An_app_with_another_protocol_is_asked_to_reload()
    {
        var response = await Post("anna", Changes(new AppData()) with { Protocol = SyncRequest.CurrentProtocol - 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(documents.Ids);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data")]
    [InlineData("text/plain")]
    public async Task Only_JSON_is_accepted_which_another_website_could_not_send_with_the_member_s_cookie(string type)
    {
        // What a form on another website can send without asking this server first. The browser would add the cookie.
        var response = await Post("anna", new StringContent("""{"protocol":1}""", Encoding.UTF8, type));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"protocol":1}""")]
    [InlineData("""{"protocol":1,"kindergeld":null,"budget":null}""")]
    [InlineData("not json")]
    public async Task An_incomplete_request_is_refused(string json)
    {
        var response = await Post("anna", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changes_that_break_the_rules_are_refused_and_nothing_is_saved()
    {
        var anna = new AppData();
        anna.Budget.SetSalary(2850);
        var invalid = Changes(anna) with
        {
            Kindergeld = new(null, new([new Entry(Guid.NewGuid(), EntryKind.Spending, Today, -50, "Kleidung", null)], [])),
        };

        var response = await Post("anna", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(documents.Ids);
    }

    private static KindergeldPot Apply(AppData phone)
    {
        var pot = new KindergeldPot();
        pot.Apply(Changes(phone).Kindergeld);
        return pot;
    }

    private sealed class MemoryDocuments : IDocumentStore
    {
        private readonly Dictionary<string, StoredDocument> stored = [];

        public IEnumerable<string> Ids => stored.Keys.Order(StringComparer.Ordinal);
        public Dictionary<string, int> Saves { get; } = [];

        /// <summary>Another phone's save, just after this request read the document.</summary>
        public Func<Task>? BeforeNextSave { get; set; }

        public Task<StoredDocument?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(stored.GetValueOrDefault(id));

        public async Task<bool> TrySaveAsync(string id, string json, int expectedVersion, CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } other)
            {
                BeforeNextSave = null;
                await other();
            }
            if ((stored.GetValueOrDefault(id)?.Version ?? 0) != expectedVersion) return false;
            stored[id] = new StoredDocument(json, expectedVersion + 1);
            Saves[id] = Saves.GetValueOrDefault(id) + 1;
            return true;
        }
    }
}
