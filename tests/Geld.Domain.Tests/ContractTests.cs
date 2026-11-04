using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Geld.Domain.Tests;

/// <summary>
/// The data that outlives a version of the app: one version writes it, another reads it (see Contracts/README.md).
/// Each sample in Contracts/ is what a released version wrote. This version must write exactly the newest sample of
/// each contract, and every sample must stay readable by the rule of its contract. Round trips can't check this: they
/// write and read with the same code, so a renamed property passes them.
/// </summary>
public class ContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Contracts");

    /// <param name="Version">What this version writes: the stored schema, or the sync protocol.</param>
    /// <param name="Reread">Reads a sample with this version's code and writes it again; null if it is refused.</param>
    /// <param name="Older">What samples of an older version must still allow.</param>
    private sealed record Contract(string Name, int Version, Func<string> Write, Func<string, string?> Reread, OlderRule Older);

    private enum OlderRule
    {
        /// <summary>Read without losing anything, like the current version: SQL rows are never migrated.</summary>
        Lossless,

        /// <summary>Loaded through a migration: data on a phone or in a backup file of an older schema.</summary>
        Migrated,

        /// <summary>Read far enough for the server to refuse the outdated app with 409, not 400.</summary>
        ReachesProtocolCheck,

        /// <summary>Nothing: the server answers only apps of its own protocol.</summary>
        NotRead,
    }

    private static readonly Contract[] Contracts =
    [
        new("local", DataStore.LocalSchema, () => DataStore.Serialize(Known.Data()), Stored, OlderRule.Migrated),
        new("backup", DataStore.BackupSchema, () => DataStore.Backup(Known.Data(), Known.Today), StoredBackup, OlderRule.Migrated),
        Api("sync-request", () => SyncRequestJson.From(Known.Request()), ApiJson.Default.SyncRequestJson,
            json => SyncRequestJson.From(json.ToDomain()), OlderRule.ReachesProtocolCheck),
        Api("sync-response", () => SyncResponseJson.From(Known.Response()), ApiJson.Default.SyncResponseJson,
            json => SyncResponseJson.From(json.ToDomain()), OlderRule.NotRead),
        Api("document-kindergeld", () => KindergeldJson.From(Known.Response().Kindergeld), DocumentJson.Default.KindergeldJson,
            json => KindergeldJson.From(json.ToDomain()), OlderRule.Lossless),
        Api("document-budget", () => BudgetJson.From(Known.Response().Budget), DocumentJson.Default.BudgetJson,
            json => BudgetJson.From(json.ToDomain()), OlderRule.Lossless),
        // Old apps read what a new server answers: a field may be added, but never renamed or removed. No domain type.
        Api("whoami", () => Known.Me, ApiJson.Default.WhoAmI, json => json, OlderRule.Lossless),
    ];

    public static TheoryData<string> Names => [.. Contracts.Select(c => c.Name)];

    public static TheoryData<string, string> Samples()
    {
        var samples = new TheoryData<string, string>();
        foreach (var contract in Contracts)
        foreach (var (_, path) in SamplesOf(contract.Name))
            samples.Add(contract.Name, Path.GetFileName(path));
        return samples;
    }

    [Theory, MemberData(nameof(Names))]
    public void This_version_writes_exactly_the_newest_sample(string name)
    {
        var contract = Contracts.Single(c => c.Name == name);
        var written = contract.Write();
        var (version, newest) = SamplesOf(name).Last();
        if (version == contract.Version && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(newest)), JsonNode.Parse(written)))
            return;

        var received = Path.Combine(Folder, name, "received.json");
        File.WriteAllText(received, Pretty(written));
        Assert.Fail($"""
            This version writes {name} differently from its newest sample, {Path.GetFileName(newest)}.
            If that is intended: make sure every older sample still passes, and raise the version if one can't. Then add
              {received}
            to tests/Geld.Domain.Tests/Contracts/{name}/ as v{contract.Version}-<release date>.json. Never edit a sample.
            """);
    }

    [Theory, MemberData(nameof(Samples))]
    public void Every_sample_stays_readable(string name, string file)
    {
        var contract = Contracts.Single(c => c.Name == name);
        var json = File.ReadAllText(Path.Combine(Folder, name, file));
        var version = VersionOf(file);
        switch (version == contract.Version ? OlderRule.Lossless : contract.Older)
        {
            case OlderRule.Lossless:
                var again = contract.Reread(json) ?? throw new Xunit.Sdk.XunitException($"{file} is refused.");
                AssertNothingLost(JsonNode.Parse(json), JsonNode.Parse(again), "");
                break;
            case OlderRule.Migrated:
                Assert.True(contract.Reread(json) is not null, $"{file} has schema {version}: add a migration to schema {contract.Version}.");
                break;
            case OlderRule.ReachesProtocolCheck:
                Assert.Equal(version, JsonSerializer.Deserialize(json, ApiJson.Default.SyncRequestJson)?.Protocol);
                break;
            case OlderRule.NotRead:
                break;
        }
    }

    // Every value in the sample is still there after reading it and writing it again. New properties may appear, and a
    // null may become a missing property: both read as null.
    private static void AssertNothingLost(JsonNode? before, JsonNode? after, string path)
    {
        switch (before)
        {
            case null:
                return;
            case JsonObject properties:
                if (after is not JsonObject again) throw Lost(path, before, after);
                foreach (var (key, value) in properties) AssertNothingLost(value, again[key], $"{path}.{key}");
                return;
            case JsonArray items:
                if (after is not JsonArray againItems || againItems.Count != items.Count) throw Lost(path, before, after);
                for (var i = 0; i < items.Count; i++) AssertNothingLost(items[i], againItems[i], $"{path}[{i}]");
                return;
            default:
                if (!JsonNode.DeepEquals(before, after)) throw Lost(path, before, after);
                return;
        }
    }

    private static Xunit.Sdk.XunitException Lost(string path, JsonNode before, JsonNode? after) =>
        new($"{(path.Length == 0 ? "The document" : path)} was {before.ToJsonString()}, but reads as {after?.ToJsonString() ?? "nothing"}.");

    private static string? Stored(string json) => DataStore.TryRead(json) is { } data ? DataStore.Serialize(data) : null;

    // A backup file is read like the stored data, but written as a backup again, on the day it records.
    private static string? StoredBackup(string json) =>
        DataStore.TryRead(json) is { LastBackup: { } day } data ? DataStore.Backup(data, day) : null;

    // Reading goes through the domain, as the server and the app do: a mapper that drops a value is caught too.
    private static Contract Api<T>(string name, Func<T> known, JsonTypeInfo<T> type, Func<T, T> throughTheDomain, OlderRule older) => new(
        name, SyncRequestJson.CurrentProtocol, () => JsonSerializer.Serialize(known(), type),
        json => JsonSerializer.Deserialize(json, type) is { } value ? JsonSerializer.Serialize(throughTheDomain(value), type) : null, older);

    // Named v<version>-<release date>.json; the newest is the last of the highest version.
    private static IEnumerable<(int Version, string Path)> SamplesOf(string contract) =>
        Directory.GetFiles(Path.Combine(Folder, contract), "v*.json")
            .Select(path => (VersionOf(Path.GetFileName(path)), path))
            .OrderBy(s => s.Item1).ThenBy(s => s.path, StringComparer.Ordinal);

    private static int VersionOf(string file) => int.Parse(file[1..file.IndexOf('-', StringComparison.Ordinal)], CultureInfo.InvariantCulture);

    private static string Pretty(string json) => JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }) + "\n";
}

/// <summary>
/// What the newest samples hold: a family that synced, then went on changing things while a sync went unanswered.
/// Fixed ids and dates, so this version always writes the same JSON.
/// </summary>
internal static class Known
{
    public static readonly DateOnly Today = new(2026, 10, 25);
    public static readonly WhoAmI Me = new("8a1c0e52", "anna@example.com", true);

    private static readonly Entry Payment = new(Guid.Parse("0e1f3d6c-6f9e-4d6d-b6b4-01b7e4501e84"), EntryKind.Payment, new(2026, 10, 12), 518, null, null);
    private static readonly Entry Jacket = new(Guid.Parse("ae0f8827-6da7-4e27-9b0d-c9a29083c3b2"), EntryKind.Spending, new(2026, 10, 14), 49.99m, "Kleidung", "Winterjacke");
    private static readonly Entry Snack = new(Guid.Parse("39931c3a-cc35-4db7-8e50-2645ee5fc17e"), EntryKind.Spending, new(2026, 10, 24), 12.5m, "Sonstiges", null);
    private static readonly Deduction Rent = new(Guid.Parse("b7a329b6-836c-49c9-9d26-54f7ce962e39"), "Miete", 1150, new MonthlyOn(1));
    private static readonly Deduction MealPlan = new(Guid.Parse("5d0f3c2a-9b7e-4c1d-8e6f-2a1b3c4d5e6f"), "Kita-Essen", 62, new EveryWeeks(4, new(2026, 10, 5)));
    private static readonly Deduction Phone = new(Guid.Parse("7f9c70f9-c3cc-4558-b7c3-20623773def0"), "Handy", 19.99m, new MonthlyOn(15));
    private static readonly PlannedSpending MakeUp = new(Guid.Parse("b12cadf9-9224-41b8-be7e-6697c30730e9"), 2026, 10, "Make-up", 45);

    private static KindergeldPot FirstPot() => new(2, 4, [Payment, Jacket]);
    private static Budget FirstBudget() => new(2850, [Rent, MealPlan, Phone], [MakeUp], new Reserve(2300, new(2026, 10, 24)));

    /// <summary>The server's documents after the first sync.</summary>
    public static SyncResponse Response() => new(FirstPot(), FirstBudget());

    public static AppData Data() => Scenario().Data;

    public static SyncRequest Request() => Scenario().Next;

    private static (AppData Data, SyncRequest Next) Scenario()
    {
        var data = new AppData(new SyncedData(FirstPot(), FirstBudget(), state: null, unconfirmed: null), lastBackup: new(2026, 10, 15));
        data.Synced.CompleteSync(data.Synced.StartSync(Me.Id), Response(), new DateTimeOffset(2026, 10, 25, 7, 45, 0, TimeSpan.Zero));

        // A spending booked with a fixed id, so that this version always writes the same JSON.
        var pot = data.Kindergeld;
        data = new AppData(new SyncedData(new(pot.Children, pot.EndDigit, [Snack, .. pot.Entries]), data.Budget, data.Synced.State, data.Synced.Unconfirmed),
            data.LastBackup);
        data.Budget.RemoveDeduction(Phone.Id);
        data.Synced.SyncFailed(data.Synced.StartSync(Me.Id));
        data.Budget.RemovePlanned(MakeUp.Id);
        data.Budget.SetReserve(2150, Today);
        data.Kindergeld.Configure(3, 4);
        return (data, data.Synced.StartSync(Me.Id).Request);
    }
}
