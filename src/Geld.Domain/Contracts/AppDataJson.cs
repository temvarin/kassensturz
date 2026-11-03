using System.Text.Json;
using System.Text.Json.Serialization;
using Geld.Domain.Sync;

namespace Geld.Domain.Contracts;

/// <summary>
/// The JSON of everything on a phone: in localStorage under <c>geld.data</c>, and in backup files without the sync state.
/// Changing a shape here changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).
/// </summary>
public sealed record AppDataJson
{
    public KindergeldJson Kindergeld { get; init; } = new();
    public BudgetJson Budget { get; init; } = new();
    public DateOnly? LastBackup { get; init; }
    public SyncStateJson? Sync { get; init; }
    public SyncRequestJson? Unconfirmed { get; init; }

    public static AppDataJson From(AppData data) => new()
    {
        Kindergeld = KindergeldJson.From(data.Kindergeld),
        Budget = BudgetJson.From(data.Budget),
        LastBackup = data.LastBackup,
        Sync = data.Synced.State is { } sync ? SyncStateJson.From(sync) : null,
        Unconfirmed = data.Synced.Unconfirmed is { } unconfirmed ? SyncRequestJson.From(unconfirmed) : null,
    };

    /// <exception cref="ArgumentException">It breaks the domain's rules.</exception>
    public AppData ToDomain() =>
        new(new SyncedData(Kindergeld.ToDomain(), Budget.ToDomain(), Sync?.ToDomain(), Unconfirmed?.ToDomain()), LastBackup);
}

/// <param name="Schema">The shape of <paramref name="Data"/>; see Storage.DataStore.</param>
public sealed record StoredJson(int Schema, AppDataJson Data);

/// <summary>The answer of <c>/api/whoami</c>: the signed-in user, and whether they are one of the family's members.</summary>
/// <param name="Id">The id Microsoft gives the user. The members list in the server's settings holds these ids.</param>
/// <param name="Name">For display only, usually the email address.</param>
public sealed record WhoAmI(string Id, string Name, bool Member);

/// <summary>
/// The API's JSON, shared by server and app. Generated at build time, so the trimmed app can read it. Strict about what arrives: a missing or null value that the types don't allow is an error,
/// not a silent default. Writes nulls, unlike <see cref="DataJson"/>: the two were set up separately, and the stored
/// shapes keep it that way.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WhoAmI))]
[JsonSerializable(typeof(SyncRequestJson))]
[JsonSerializable(typeof(SyncResponseJson))]
public sealed partial class ApiJson : JsonSerializerContext;

/// <summary>
/// The JSON of the synced documents as the server stores them (<c>dbo.Documents</c>, kept with six months of history
/// and read by <c>db/restore.sql</c>). Set up exactly like <see cref="ApiJson"/>, which wrote them until 2026-11-03, so
/// the rows didn't change; kept apart so that a change to how the API sends its answers can't change rows that already
/// exist.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(KindergeldJson))]
[JsonSerializable(typeof(BudgetJson))]
public sealed partial class DocumentJson : JsonSerializerContext;

/// <summary>The JSON of the data on a phone and in backup files (Storage.DataStore). Leaves nulls out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(StoredJson))]
internal sealed partial class DataJson : JsonSerializerContext;
