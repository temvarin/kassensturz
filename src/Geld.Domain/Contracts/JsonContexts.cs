using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geld.Domain.Contracts;

// One serializer setup per boundary, generated at build time so that the trimmed app can use it.

/// <summary>
/// The API's JSON. A missing or null value that the types don't
/// allow is an error, not a silent default. It writes nulls and <see cref="DataJson"/> doesn't; the first versions
/// were set up that way, and the stored shapes keep it.
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
/// The rows in <c>dbo.Documents</c>, which are kept for six months of history. It is set up like <see cref="ApiJson"/>,
/// which wrote them until 2026-11-03, and kept apart so that a change to the API can't change existing rows.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(KindergeldJson))]
[JsonSerializable(typeof(BudgetJson))]
public sealed partial class DocumentJson : JsonSerializerContext;

/// <summary>The data on a phone and in backup files. It leaves nulls out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(StoredJson))]
internal sealed partial class DataJson : JsonSerializerContext;

internal static class Json
{
    /// <summary>JSON can give a list item as null even though the type doesn't allow it.</summary>
    public static T Item<T>(T? item) where T : class => item ?? throw new ArgumentException("An item of a list is missing (null).");
}
