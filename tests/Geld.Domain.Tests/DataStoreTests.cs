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
        data.MarkBackedUp(Today);
        new DataStore(memory).Save(data);

        var loaded = new DataStore(memory).Load();

        Assert.Equal((2, 4), (loaded.Kindergeld.Children, loaded.Kindergeld.EndDigit));
        Assert.Equal([jacket, payment], loaded.Kindergeld.Entries);
        Assert.Equal(2850m, loaded.Budget.NetSalary);
        Assert.Equal([rent], loaded.Budget.Deductions);
        Assert.Equal([makeUp], loaded.Budget.Planned);
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
    [InlineData("""{"schema":2,"data":{}}""")] // from a newer version of the app
    [InlineData("null")]
    public void A_file_that_is_not_a_backup_of_this_version_is_rejected(string json) =>
        Assert.Null(DataStore.TryRead(json));

    [Fact]
    public void Unreadable_stored_data_does_not_crash_the_app()
    {
        var memory = new MemoryStore(new() { ["geld.data"] = "{not json" });

        Assert.True(new DataStore(memory).Load().IsEmpty);
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
}
