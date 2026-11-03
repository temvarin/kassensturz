using System.Text.Json;
using System.Text.Json.Nodes;
using Geld.Domain.Contracts;

namespace Geld.Domain.Storage;

/// <summary>A string store such as the browser's localStorage.</summary>
public interface IKeyValueStore
{
    string? Read(string key);
    void Write(string key, string value);
    void Remove(string key);
}

/// <summary>
/// Saves <see cref="AppData"/> as JSON (<see cref="AppDataJson"/>); a backup file has the same format without the sync
/// state. The schema number marks the shape: when it changes, add a migration from the previous schema here instead of
/// discarding the user's data. The stored data and backup files have a schema each, because they live for different
/// times: a backup keeps its schema as long as its own shape stays, so older versions of the app can still load it.
/// The tests keep samples of every released shape (tests/Geld.Domain.Tests/Contracts).
/// </summary>
public sealed class DataStore(IKeyValueStore store)
{
    private const string Key = "geld.data";
    private const string UnreadableKey = "geld.data.unreadable";
    /// <summary>2 since 2026-11-03: the unanswered budget changes keep the salary and the Rücklage as one settings record.</summary>
    internal const int LocalSchema = 2;

    /// <summary>Still 1: a backup has no unanswered changes, so its shape didn't change with schema 2.</summary>
    internal const int BackupSchema = 1;

    /// <summary>
    /// The stored data. If it can't be read (damaged, or written by a version of the app this one doesn't know, e.g.
    /// after a rollback), the app starts fresh rather than crash, but the next save would overwrite what it couldn't
    /// read: so that is kept aside first, see <see cref="Unreadable"/>.
    /// </summary>
    /// <returns>Empty data if nothing is stored or it can't be read.</returns>
    public AppData Load()
    {
        if (store.Read(Key) is not { } json) return new AppData();
        if (TryRead(json) is { } data) return data;
        // Only the first: what is stored after it is the fresh start, worth less than the data it replaced.
        if (Unreadable is null) store.Write(UnreadableKey, json);
        return new AppData();
    }

    /// <summary>Stored data that couldn't be read, exactly as it was stored; null if there is none.</summary>
    public string? Unreadable => store.Read(UnreadableKey);

    public void DiscardUnreadable() => store.Remove(UnreadableKey);

    public void Save(AppData data) => store.Write(Key, Serialize(data));

    public static string Serialize(AppData data) => Write(LocalSchema, AppDataJson.From(data));

    /// <summary>
    /// The contents of a backup file made today. The file records its own date, so data restored from it
    /// counts as backed up on that day. It leaves out the sync state, which belongs to this device.
    /// <paramref name="data"/> itself is unchanged until the file is saved.
    /// </summary>
    public static string Backup(AppData data, DateOnly today) =>
        Write(BackupSchema, AppDataJson.From(data) with { LastBackup = today, Sync = null, Unconfirmed = null });

    /// <summary>Reads a backup file, or the stored data.</summary>
    /// <returns>
    /// null if it isn't one, comes from a version of the app this one doesn't know, or breaks the app's rules (e.g. a
    /// file edited by hand or damaged): the data is restored through the same rules as entering it.
    /// </returns>
    public static AppData? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DataJson.Default.StoredJson) switch
            {
                { Schema: LocalSchema, Data: { } data } => data.ToDomain(),
                { Schema: 1, Data: { } data } => FromSchema1(data, json).ToDomain(),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Schema 1 kept the salary and the Rücklage of unanswered budget changes as two fields (sync protocol 2); schema 2
    /// keeps them as one settings record. Read as schema 2, those fields are skipped, so they are taken from the stored
    /// JSON here. Only which settings were touched matters: they are sent again as they are now.
    /// </summary>
    private static AppDataJson FromSchema1(AppDataJson data, string json) =>
        data.Unconfirmed is { } unconfirmed
        && JsonNode.Parse(json)?["data"]?["unconfirmed"]?["budget"] is JsonObject budget
        && (budget["netSalary"] is not null || budget["reserve"] is not null)
            ? data with
            {
                Unconfirmed = unconfirmed with
                {
                    Budget = unconfirmed.Budget with { Settings = new(data.Budget.NetSalary, data.Budget.Reserve) },
                },
            }
            : data;

    private static string Write(int schema, AppDataJson data) =>
        JsonSerializer.Serialize(new StoredJson(schema, data), DataJson.Default.StoredJson);
}
