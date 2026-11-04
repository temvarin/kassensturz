using System.Data;
using Microsoft.Data.SqlClient;

namespace Geld.Server.Sync;

public sealed record StoredDocument(string Json, int Version);

/// <summary>
/// The synced documents: <c>kindergeld</c>, shared by the family, and <c>budget:{user id}</c>, one per member. They are
/// small and always read and written whole.
/// </summary>
public interface IDocumentStore
{
    Task<StoredDocument?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves only if the document is still at <paramref name="expectedVersion"/> (0 for a new one), so two phones saving at
    /// once can't overwrite each other unnoticed.
    /// </summary>
    /// <returns>false if someone else saved first. Then read again, apply the change again and retry.</returns>
    Task<bool> TrySaveAsync(string id, string json, int expectedVersion, CancellationToken cancellationToken = default);
}

/// <summary>The documents in Azure SQL (db/schema.sql).</summary>
public sealed class DocumentStore(string connectionString) : IDocumentStore
{
    // Azure SQL is briefly unavailable now and then (maintenance, failover). SqlClient retries opening on such errors,
    // 40613 "database not currently available" among them.
    private static readonly SqlRetryLogicBaseProvider Retry = SqlConfigurableRetryFactory.CreateFixedRetryProvider(
        new SqlRetryLogicOption { NumberOfTries = 8, DeltaTime = TimeSpan.FromSeconds(10) });

    public async Task<StoredDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, "SELECT Json, Version FROM dbo.Documents WHERE Id = @Id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StoredDocument(reader.GetString(0), reader.GetInt32(1)) : null;
    }

    public async Task<bool> TrySaveAsync(string id, string json, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, expectedVersion == 0
            ? "INSERT INTO dbo.Documents (Id, Json, Version) VALUES (@Id, @Json, 1)"
            : "UPDATE dbo.Documents SET Json = @Json, Version = Version + 1 WHERE Id = @Id AND Version = @Version", id);
        command.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = json;
        if (expectedVersion != 0) command.Parameters.Add("@Version", SqlDbType.Int).Value = expectedVersion;
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (SqlException e) when (e.Number == 2627) // primary key: someone else created the document first
        {
            return false;
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString) { RetryLogicProvider = Retry };
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static SqlCommand Command(SqlConnection connection, string sql, string id)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 200).Value = id;
        return command;
    }
}
