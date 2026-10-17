using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>The answer of <c>/api/whoami</c>: the signed-in user, and whether they are one of the family's members.</summary>
/// <param name="Id">The id Microsoft gives the user. The members list in the server's settings holds these ids.</param>
/// <param name="Name">For display only, usually the email address.</param>
public sealed record WhoAmI(string Id, string Name, bool Member);

/// <summary>The API's JSON, shared by server and app. Generated at build time, so the trimmed app can read it.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WhoAmI))]
public sealed partial class ApiJson : JsonSerializerContext;
