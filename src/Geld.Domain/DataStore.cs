using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>A string store such as the browser's localStorage.</summary>
public interface IKeyValueStore
{
    string? Read(string key);
    void Write(string key, string value);
}

/// <summary>
/// Saves <see cref="AppData"/> as JSON; a backup file has the same format. The schema number marks the stored shape:
/// when it changes, add a migration from the previous schema here instead of discarding the user's data.
/// </summary>
public sealed class DataStore(IKeyValueStore store)
{
    private const string Key = "geld.data";
    private const int Schema = 1;

    /// <returns>Empty data if nothing is stored, or if what is stored can't be read: start fresh rather than crash.</returns>
    public AppData Load() => store.Read(Key) is { } json && TryRead(json) is { } data ? data : new AppData();

    public void Save(AppData data) => store.Write(Key, Serialize(data));

    public static string Serialize(AppData data) =>
        JsonSerializer.Serialize(new StoredData(Schema, data), DataJson.Default.StoredData);

    /// <summary>
    /// The contents of a backup file made today. The file records its own date, so data restored from it
    /// counts as backed up on that day. It leaves out the sync state, which belongs to this device.
    /// <paramref name="data"/> itself is unchanged until the file is saved.
    /// </summary>
    public static string Backup(AppData data, DateOnly today)
    {
        var (lastBackup, sync) = (data.LastBackup, data.Sync);
        (data.LastBackup, data.Sync) = (today, null);
        try
        {
            return Serialize(data);
        }
        finally
        {
            (data.LastBackup, data.Sync) = (lastBackup, sync);
        }
    }

    /// <summary>Reads a backup file.</summary>
    /// <returns>null if it isn't one, or comes from a version of the app this one doesn't know.</returns>
    public static AppData? TryRead(string json)
    {
        try
        {
            var stored = JsonSerializer.Deserialize(json, DataJson.Default.StoredData);
            return stored is { Schema: Schema, Data: { } data } ? data : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record StoredData(int Schema, AppData Data);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StoredData))]
internal sealed partial class DataJson : JsonSerializerContext;
