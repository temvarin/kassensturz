using Geld.Server.Sync;

namespace Geld.Testing;

/// <summary>
/// The documents in memory instead of in Azure SQL: the one stand-in for the database, used by the server's and the
/// app's tests. It passes the same tests as the real store (DocumentStoreContract), so it can't drift from it.
/// </summary>
public sealed class MemoryDocumentStore : IDocumentStore
{
    private readonly Dictionary<string, StoredDocument> stored = [];

    public IEnumerable<string> Ids => stored.Keys.Order(StringComparer.Ordinal);

    /// <summary>How often each document was saved.</summary>
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
