using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Geld.Server.Tests;

/// <summary>
/// What every document store must do: the store in Azure SQL, and the stand-in the other tests use
/// (MemoryDocumentStore). A stand-in that passes the same tests can't drift from the real thing.
/// </summary>
public abstract class DocumentStoreContract
{
    protected abstract IDocumentStore Store { get; }

    /// <summary>Unique per test: the database is shared by the tests of a class.</summary>
    protected string Id { get; } = $"test:{Guid.NewGuid():N}";

    [Fact]
    public async Task A_document_that_was_never_saved_does_not_exist()
    {
        Assert.Null(await Store.GetAsync(Id));
    }

    [Fact]
    public async Task A_new_document_starts_at_version_1_and_every_save_raises_it()
    {
        Assert.True(await Store.TrySaveAsync(Id, """{"n":1}""", expectedVersion: 0));
        Assert.Equal(new StoredDocument("""{"n":1}""", 1), await Store.GetAsync(Id));

        Assert.True(await Store.TrySaveAsync(Id, """{"n":2}""", expectedVersion: 1));
        Assert.Equal(new StoredDocument("""{"n":2}""", 2), await Store.GetAsync(Id));
    }

    [Fact]
    public async Task A_save_based_on_an_outdated_version_is_refused_instead_of_overwriting_the_other()
    {
        await Store.TrySaveAsync(Id, """{"n":1}""", expectedVersion: 0);
        // Both phones read version 1; this one saves first.
        Assert.True(await Store.TrySaveAsync(Id, """{"phone":"A"}""", expectedVersion: 1));

        Assert.False(await Store.TrySaveAsync(Id, """{"phone":"B"}""", expectedVersion: 1));
        Assert.Equal(new StoredDocument("""{"phone":"A"}""", 2), await Store.GetAsync(Id));
    }

    [Fact]
    public async Task Creating_a_document_someone_else_just_created_is_refused()
    {
        Assert.True(await Store.TrySaveAsync(Id, """{"phone":"A"}""", expectedVersion: 0));

        Assert.False(await Store.TrySaveAsync(Id, """{"phone":"B"}""", expectedVersion: 0));
        Assert.Equal("""{"phone":"A"}""", (await Store.GetAsync(Id))?.Json);
    }
}

public class MemoryDocumentStoreTests : DocumentStoreContract
{
    protected override IDocumentStore Store { get; } = new MemoryDocumentStore();
}

/// <summary>The store in SQL Server, and what only a database does: check the JSON, keep the history.</summary>
public class DocumentStoreTests(SqlDatabase database) : DocumentStoreContract, IClassFixture<SqlDatabase>
{
    private readonly DocumentStore store = new(database.ConnectionString);

    protected override IDocumentStore Store => store;

    [Fact]
    public async Task The_database_accepts_only_JSON()
    {
        await Assert.ThrowsAsync<SqlException>(() => store.TrySaveAsync(Id, "not json", expectedVersion: 0));
    }

    [Fact]
    public async Task The_restore_script_brings_back_a_document_as_it_was_before_a_mistake()
    {
        await store.TrySaveAsync(Id, """{"entries":["Oktober","November"]}""", expectedVersion: 0);
        var beforeTheMistake = await database.ExecuteAsync("SELECT SYSUTCDATETIME()");
        await store.TrySaveAsync(Id, """{"entries":["Oktober"]}""", expectedVersion: 1);
        Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.Documents FOR SYSTEM_TIME ALL WHERE Id = @Id", new SqlParameter("@Id", Id)));

        // The script as written, with the values the admin would fill in.
        var script = Regex.Replace(SqlDatabase.Script("restore.sql"), @"^DECLARE .*$", "", RegexOptions.Multiline);
        var restored = await database.ExecuteAsync(script, new SqlParameter("@Id", Id), new SqlParameter("@At", beforeTheMistake));

        Assert.Equal(1, restored);
        Assert.Equal(new StoredDocument("""{"entries":["Oktober","November"]}""", 3), await store.GetAsync(Id));
    }

    [Fact]
    public void The_published_server_keeps_the_culture_data_the_database_driver_needs()
    {
        // These tests run the server inside the test process, which has its own settings; the server's own, as
        // App Service starts it, are in its runtimeconfig.json.
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Geld.Server.runtimeconfig.json")));

        Assert.NotEqual(true, settings?["runtimeOptions"]?["configProperties"]?["System.Globalization.Invariant"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Members_can_see_that_the_database_answers()
    {
        using var server = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("WEBSITE_AUTH_ENABLED", "True")
            .UseSetting("Geld:Members", "member")
            .UseSetting("ConnectionStrings:Geld", database.ConnectionString));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/status", UriKind.Relative));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", "member");

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("""{"kindergeldVersion":""", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
