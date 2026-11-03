using System.Text.Json.Nodes;

namespace Geld.Domain.Tests;

public class DataStoreTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    /// <summary>In-memory stand-in for localStorage.</summary>
    private sealed class MemoryStore(Dictionary<string, string>? initial = null) : IKeyValueStore
    {
        public Dictionary<string, string> Data { get; } = initial ?? [];
        public string? Read(string key) => Data.GetValueOrDefault(key);
        public void Write(string key, string value) => Data[key] = value;
        public void Remove(string key) => Data.Remove(key);
    }

    [Fact]
    public void Starts_empty()
    {
        var data = new DataStore(new MemoryStore()).Load();

        Assert.True(data.IsEmpty);
        Assert.Null(data.LastBackup);
    }

    [Fact]
    public void Saved_data_loads_back_unchanged()
    {
        var memory = new MemoryStore();
        var data = new AppData();
        data.Kindergeld.Configure(children: 2, endDigit: 4);
        var payment = data.Kindergeld.Receive(Today, 518, "Oktober");
        var jacket = data.Kindergeld.Spend(Today, 49.99m, "Kleidung", "Winterjacke");
        data.Budget.SetSalary(2850);
        var rent = data.Budget.AddDeduction("Miete", 1150, 1);
        var makeUp = data.Budget.Plan(2026, 10, "Make-up", 45);
        data.Budget.SetReserve(2300, Today);
        data.MarkBackedUp(Today);
        new DataStore(memory).Save(data);

        var loaded = new DataStore(memory).Load();

        Assert.Equal((2, 4), (loaded.Kindergeld.Children, loaded.Kindergeld.EndDigit));
        Assert.Equal([jacket, payment], loaded.Kindergeld.Entries);
        Assert.Equal(2850m, loaded.Budget.NetSalary);
        Assert.Equal([rent], loaded.Budget.Deductions);
        Assert.Equal([makeUp], loaded.Budget.Planned);
        Assert.Equal(new Reserve(2300, Today), loaded.Budget.Reserve);
        Assert.Equal(Today, loaded.LastBackup);
    }

    [Fact]
    public void A_backup_file_reads_back_as_the_same_data()
    {
        var data = new AppData();
        data.Kindergeld.Receive(Today, 518);

        var restored = DataStore.TryRead(DataStore.Serialize(data));

        Assert.Equal(data.Kindergeld.Entries, restored?.Kindergeld.Entries);
    }

    [Fact]
    public void Data_restored_from_a_backup_counts_as_backed_up_on_the_day_of_the_file()
    {
        var data = new AppData();
        data.Kindergeld.Receive(Today, 518);

        var file = DataStore.Backup(data, Today);

        Assert.Null(data.LastBackup); // not until the file is actually saved
        Assert.Equal(Today, DataStore.TryRead(file)?.LastBackup);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"some":"other file"}""")]
    [InlineData("""{"schema":3,"data":{}}""")] // from a newer version of the app
    [InlineData("null")]
    public void A_file_that_is_not_a_backup_of_this_version_is_rejected(string json) =>
        Assert.Null(DataStore.TryRead(json));

    [Theory]
    [InlineData("""{"schema":1,"data":{"kindergeld":null,"budget":null}}""")]
    [InlineData("""{"schema":1,"data":{"kindergeld":{"children":0},"budget":{}}}""")]
    [InlineData("""{"schema":1,"data":{"kindergeld":{},"budget":{"netSalary":-100}}}""")]
    [InlineData("""{"schema":1,"data":{"kindergeld":{},"budget":{"reserve":{"amount":-1,"asOf":"2026-10-24"}}}}""")]
    [InlineData("""{"schema":1,"data":{"kindergeld":{"entries":[null]},"budget":{}}}""")]
    [InlineData("""{"schema":1,"data":{"kindergeld":{"entries":[{"id":"8c3f0b4e-1d2a-4c5b-9e6f-7a8b9c0d1e2f","kind":"Payment","date":"2026-10-14","amount":-518}]},"budget":{}}}""")]
    public void A_file_that_breaks_the_app_s_rules_is_rejected(string json) =>
        Assert.Null(DataStore.TryRead(json));

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"schema":3,"data":{"kindergeld":{},"budget":{}}}""")] // after a rollback to an older version
    [InlineData("""{"schema":1,"data":{"kindergeld":{"children":0},"budget":{}}}""")]
    public void Unreadable_stored_data_is_kept_aside_and_the_app_starts_fresh(string stored)
    {
        var memory = new MemoryStore(new() { ["geld.data"] = stored });
        var store = new DataStore(memory);

        var data = store.Load();
        data.Kindergeld.Receive(Today, 518);
        store.Save(data);

        Assert.Equal(stored, store.Unreadable);
        Assert.Equal(518m, store.Load().Kindergeld.Received);
    }

    [Fact]
    public void Readable_or_missing_data_keeps_nothing_aside()
    {
        var memory = new MemoryStore();
        var store = new DataStore(memory);
        store.Load();
        store.Save(new AppData());
        store.Load();

        Assert.Null(store.Unreadable);
    }

    [Fact]
    public void Data_kept_aside_is_not_replaced_by_later_unreadable_data()
    {
        var memory = new MemoryStore(new() { ["geld.data"] = "{the original" });
        var store = new DataStore(memory);
        store.Load();
        memory.Data["geld.data"] = "{damaged again";

        store.Load();

        Assert.Equal("{the original", store.Unreadable);
    }

    [Fact]
    public void Data_kept_aside_can_be_discarded()
    {
        var store = new DataStore(new MemoryStore(new() { ["geld.data"] = "{not json" }));
        store.Load();

        store.DiscardUnreadable();

        Assert.Null(store.Unreadable);
    }

    [Fact]
    public void A_backup_is_due_as_soon_as_there_is_data_and_again_after_30_days()
    {
        var data = new AppData();
        Assert.False(data.NeedsBackup(Today)); // nothing to lose

        data.Kindergeld.Receive(Today, 518);
        Assert.True(data.NeedsBackup(Today));

        data.MarkBackedUp(Today);
        Assert.False(data.NeedsBackup(Today.AddDays(29)));
        Assert.True(data.NeedsBackup(Today.AddDays(30)));
    }

    [Fact]
    public void Unanswered_settings_stored_by_schema_1_are_still_sent_again()
    {
        // As the release of 2026-10-25 stored it had the lost sync changed the Rücklage (2300 → 2150): its sample, with
        // the change added in that release's shape. No released sample has one; the file itself is never edited.
        var sample = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "local", "v1-2026-10-25.json")))!;
        sample["data"]!["unconfirmed"]!["budget"]!["reserve"] = JsonNode.Parse("""{"value":{"amount":2150,"asOf":"2026-10-25"}}""");
        var data = DataStore.TryRead(sample.ToJsonString())!;
        // Back to what the server last confirmed: only the lost answer's changes can tell that the settings must go out
        // again, since the server may have saved 2150.
        data.Budget.SetReserve(2300, new DateOnly(2026, 10, 24));

        var settings = data.Synced.StartSync("8a1c0e52").Request.Budget.Settings;

        Assert.Equal(new BudgetSettings(2850, new Reserve(2300, new DateOnly(2026, 10, 24))), settings);
    }

    public static TheoryData<string, Action<AppData>> SingleThings => new()
    {
        { "an entry", data => data.Kindergeld.Receive(Today, 518) },
        { "the children", data => data.Kindergeld.Configure(2, null) },
        { "the Endziffer", data => data.Kindergeld.Configure(1, 4) },
        { "the salary", data => data.Budget.SetSalary(2500) },
        { "a fixed cost", data => data.Budget.AddDeduction("Miete", 900, 1) },
        { "a planned spending", data => data.Budget.Plan(2026, 10, "Make-up", 30) },
        { "the Rücklage", data => data.Budget.SetReserve(1000, Today) },
    };

    [Theory, MemberData(nameof(SingleThings))]
    public void Anything_the_user_entered_counts_as_data_to_lose(string thing, Action<AppData> enter)
    {
        var data = new AppData();
        enter(data);

        Assert.False(data.IsEmpty, $"Only {thing} entered, yet the data counts as empty.");
        Assert.True(data.NeedsBackup(Today));
    }
}
