using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Geld.Server.Tests;

public class DocumentStoreTests(SqlDatabase database) : IClassFixture<SqlDatabase>
{
    private readonly DocumentStore store = new(database.ConnectionString);
    private readonly string id = $"test:{Guid.NewGuid():N}"; // the database is shared by the tests of this class

    [Fact]
    public async Task A_document_that_was_never_saved_does_not_exist()
    {
        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task A_new_document_starts_at_version_1_and_every_save_raises_it()
    {
        Assert.True(await store.TrySaveAsync(id, """{"n":1}""", expectedVersion: 0));
        Assert.Equal(new StoredDocument("""{"n":1}""", 1), await store.GetAsync(id));

        Assert.True(await store.TrySaveAsync(id, """{"n":2}""", expectedVersion: 1));
        Assert.Equal(new StoredDocument("""{"n":2}""", 2), await store.GetAsync(id));
    }

    [Fact]
    public async Task A_save_based_on_an_outdated_version_is_refused_instead_of_overwriting_the_other()
    {
        await store.TrySaveAsync(id, """{"n":1}""", expectedVersion: 0);
        // Both phones read version 1; this one saves first.
        Assert.True(await store.TrySaveAsync(id, """{"phone":"A"}""", expectedVersion: 1));

        Assert.False(await store.TrySaveAsync(id, """{"phone":"B"}""", expectedVersion: 1));
        Assert.Equal(new StoredDocument("""{"phone":"A"}""", 2), await store.GetAsync(id));
    }

    [Fact]
    public async Task Creating_a_document_someone_else_just_created_is_refused()
    {
        Assert.True(await store.TrySaveAsync(id, """{"phone":"A"}""", expectedVersion: 0));

        Assert.False(await store.TrySaveAsync(id, """{"phone":"B"}""", expectedVersion: 0));
        Assert.Equal("""{"phone":"A"}""", (await store.GetAsync(id))?.Json);
    }

    [Fact]
    public async Task The_database_accepts_only_JSON()
    {
        await Assert.ThrowsAsync<SqlException>(() => store.TrySaveAsync(id, "not json", expectedVersion: 0));
    }

    [Fact]
    public async Task The_restore_script_brings_back_a_document_as_it_was_before_a_mistake()
    {
        await store.TrySaveAsync(id, """{"entries":["Oktober","November"]}""", expectedVersion: 0);
        var beforeTheMistake = await database.ExecuteAsync("SELECT SYSUTCDATETIME()");
        await store.TrySaveAsync(id, """{"entries":["Oktober"]}""", expectedVersion: 1);
        Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.Documents FOR SYSTEM_TIME ALL WHERE Id = @Id", new SqlParameter("@Id", id)));

        // The script as written, with the values the admin would fill in.
        var script = Regex.Replace(SqlDatabase.Script("restore.sql"), @"^DECLARE .*$", "", RegexOptions.Multiline);
        var restored = await database.ExecuteAsync(script, new SqlParameter("@Id", id), new SqlParameter("@At", beforeTheMistake));

        Assert.Equal(1, restored);
        Assert.Equal(new StoredDocument("""{"entries":["Oktober","November"]}""", 3), await store.GetAsync(id));
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
