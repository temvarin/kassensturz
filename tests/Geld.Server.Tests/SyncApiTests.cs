using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.Server.Tests;

/// <summary><c>POST /api/sync</c>, with the documents in memory (<see cref="DocumentStoreTests"/> covers the database).</summary>
public class SyncApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private readonly MemoryDocumentStore documents = new();

    private HttpClient? client;

    private HttpClient Server() => client ??= factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("WEBSITE_AUTH_ENABLED", "True");
        host.UseSetting("Geld:Members", "anna,ben");
        host.ConfigureTestServices(services => services.AddSingleton<IDocumentStore>(documents));
    }).CreateClient();

    private static SyncRequest Changes(AppData phone, string user = "anna") => phone.Synced.StartSync(user).Request;

    private async Task<HttpResponseMessage> Post(string user, HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sync", UriKind.Relative)) { Content = content };
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user);
        return await Server().SendAsync(request);
    }

    private Task<HttpResponseMessage> Post(string user, SyncRequest sync) => Post(user, SyncRequestJson.From(sync));

    private Task<HttpResponseMessage> Post(string user, SyncRequestJson sync) => Post(user, JsonContent.Create(sync, ApiJson.Default.SyncRequestJson));

    private async Task<SyncResponse> Sync(string user, SyncRequest sync)
    {
        var response = await Post(user, sync);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync(ApiJson.Default.SyncResponseJson))!.ToDomain();
    }

    [Fact]
    public async Task The_documents_are_stored_as_DocumentJson_writes_them_not_as_the_api_sends_them()
    {
        // The rows outlive the API's settings (six months of history): today both write the same JSON, and this keeps
        // the server from storing in the API's shape once they differ.
        var anna = new AppData();
        anna.Kindergeld.Receive(Today, 518); // a note and a category of null
        anna.Budget.SetSalary(2850); // and no Rücklage
        await Sync("anna", Changes(anna));

        var pot = (await documents.GetAsync("kindergeld"))!.Json;
        var budget = (await documents.GetAsync("budget:anna"))!.Json;

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize(pot, DocumentJson.Default.KindergeldJson)!, DocumentJson.Default.KindergeldJson), pot);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize(budget, DocumentJson.Default.BudgetJson)!, DocumentJson.Default.BudgetJson), budget);
    }

    [Fact]
    public async Task The_pot_is_shared_and_each_budget_belongs_to_its_member()
    {
        var anna = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        anna.Budget.SetSalary(2850);
        await Sync("anna", Changes(anna));

        var ben = await Sync("ben", Changes(new AppData(), "ben"));

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
            System.Text.Json.JsonSerializer.Serialize(KindergeldJson.From(Apply(anna)), DocumentJson.Default.KindergeldJson), 0);

        var answer = await Sync("ben", Changes(ben, "ben"));

        Assert.Equal([jacket, payment], answer.Kindergeld.Entries);
    }

    [Fact]
    public async Task An_app_with_another_protocol_is_asked_to_reload()
    {
        // As an older app sends it: an older protocol, and no user id.
        var response = await Post("anna", SyncRequestJson.From(Changes(new AppData())) with { Protocol = SyncRequestJson.CurrentProtocol - 1, UserId = null });

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
    public async Task A_request_for_another_account_than_the_one_signed_in_is_refused_before_anything_is_saved()
    {
        // The app still thinks Anna is signed in, but Ben signed in since (in another tab, on the same device).
        var anna = new AppData();
        anna.Budget.SetSalary(2850);

        var response = await Post("ben", Changes(anna, "anna"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Empty(documents.Ids);
    }

    [Fact]
    public async Task A_valid_pot_change_with_an_invalid_budget_change_saves_neither()
    {
        var anna = new AppData();
        anna.Kindergeld.Receive(Today, 518);

        var response = await Post("anna", Changes(anna) with { Budget = new(new BudgetSettings(-100, null), new([], []), new([], [])) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(documents.Ids);
    }

    [Fact]
    public async Task A_missing_item_is_refused_as_a_bad_request()
    {
        var json = $$"""
            {"protocol":{{SyncRequestJson.CurrentProtocol}},"userId":"anna",
             "kindergeld":{"settings":null,"entries":{"changed":[null],"removed":[] } },
             "budget":{"deductions":{"changed":[],"removed":[]},"planned":{"changed":[],"removed":[]},"settings":null } }
            """;

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

    private static KindergeldPot Apply(AppData phone) => KindergeldSync.Apply(new KindergeldPot(), Changes(phone).Kindergeld);
}
