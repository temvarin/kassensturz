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
/// Saves <see cref="AppData"/> as JSON, and writes and reads backup files, which have the same format without the sync
/// state. When a shape changes, its schema goes up and a migration from the previous one goes here. The stored data
/// and backup files have a schema each, so a backup keeps its schema, and older versions can load it, for as long as
/// its own shape stays.
/// </summary>
public sealed class DataStore(IKeyValueStore store)
{
    private const string Key = "geld.data";
    private const string UnreadableKey = "geld.data.unreadable";
    /// <summary>
    /// 2 since 2026-11-03, when the unanswered budget changes got one settings record; 3 since 2026-11-04, when fixed
    /// costs could repeat every few weeks. An older version refuses schema 3 rather than turning such a cost into a
    /// monthly one.
    /// </summary>
    internal const int LocalSchema = 3;

    /// <summary>2 since 2026-11-04, for fixed costs every few weeks. Schema 2 of the stored data didn't touch backups.</summary>
    internal const int BackupSchema = 2;

    /// <summary>
    /// The stored data. Data this version can't read (damaged, or written by a newer version before a rollback) is kept
    /// aside in <see cref="Unreadable"/> first, because the app starts fresh and its next save would overwrite it.
    /// </summary>
    public AppData Load()
    {
        if (store.Read(Key) is not { } json) return new AppData();
        if (TryRead(json) is { } data) return data;
        // Only the first: anything unreadable after it is a fresh start, worth less than what it replaced.
        if (Unreadable is null) store.Write(UnreadableKey, json);
        return new AppData();
    }

    /// <summary>Stored data that couldn't be read, exactly as it was stored.</summary>
    public string? Unreadable => store.Read(UnreadableKey);

    public void DiscardUnreadable() => store.Remove(UnreadableKey);

    public void Save(AppData data) => store.Write(Key, Serialize(data));

    public static string Serialize(AppData data) => Write(LocalSchema, AppDataJson.From(data));

    /// <summary>
    /// A backup file made today. It records its own date, so data restored from it counts as backed up on that day, and
    /// it leaves out the sync state, which belongs to this device.
    /// </summary>
    public static string Backup(AppData data, DateOnly today) =>
        Write(BackupSchema, AppDataJson.From(data) with { LastBackup = today, Sync = null, Unconfirmed = null });

    /// <summary>Reads a backup file or the stored data, through the same rules as entering data.</summary>
    /// <returns>null if it isn't ours, comes from an unknown version, or breaks the rules (e.g. edited by hand).</returns>
    public static AppData? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, DataJson.Default.StoredJson) switch
            {
                { Schema: 2 or 3, Data: { } data } => data.ToDomain(), // 3 only added optional fields
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
    /// Schema 1 kept the salary and the Rücklage of unanswered budget changes as two fields; schema 2 has one settings
    /// record. Only whether they were touched matters, since touched settings are sent again as they are now.
    /// </summary>
    private static AppDataJson FromSchema1(AppDataJson data, string json)
    {
        if (data.Unconfirmed is not { } unconfirmed) return data;
        var budget = JsonNode.Parse(json)?["data"]?["unconfirmed"]?["budget"];
        var settingsTouched = budget?["netSalary"] is not null || budget?["reserve"] is not null;
        if (!settingsTouched) return data;

        var settings = new BudgetSettingsJson(data.Budget.NetSalary, data.Budget.Reserve);
        return data with { Unconfirmed = unconfirmed with { Budget = unconfirmed.Budget with { Settings = settings } } };
    }

    private static string Write(int schema, AppDataJson data) =>
        JsonSerializer.Serialize(new StoredJson(schema, data), DataJson.Default.StoredJson);
}
