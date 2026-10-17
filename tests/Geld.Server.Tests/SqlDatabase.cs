using Microsoft.Data.SqlClient;

namespace Geld.Server.Tests;

/// <summary>
/// A fresh database with db/schema.sql applied, deleted afterwards. On SQL Server LocalDB, or on the server in the
/// environment variable GELD_TEST_SQL (CI runs one in a container).
/// </summary>
public sealed class SqlDatabase : IAsyncLifetime
{
    private static readonly string Server = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("GELD_TEST_SQL") ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true")
    { TrustServerCertificate = true }.ConnectionString;

    private readonly string name = $"GeldTest_{Guid.NewGuid():N}";

    public string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = name }.ConnectionString;

    public static string Script(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "db", file));

    public async Task InitializeAsync()
    {
        await ExecuteAsync(Server, $"CREATE DATABASE [{name}]");
        await ExecuteAsync(ConnectionString, Script("schema.sql"));
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(Server, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    }

    /// <summary>Runs SQL on this database and returns the first value of the result.</summary>
    public Task<object?> ExecuteAsync(string sql, params SqlParameter[] parameters) => ExecuteAsync(ConnectionString, sql, parameters);

    private static async Task<object?> ExecuteAsync(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }
}
