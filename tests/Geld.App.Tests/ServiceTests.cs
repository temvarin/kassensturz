using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

/// <summary>What the user does, through the services the pages call, without rendering a page.</summary>
public class ServiceTests : AppTestContext
{
    private AppState State => Services.GetRequiredService<AppState>();
    private KindergeldService Kindergeld => Services.GetRequiredService<KindergeldService>();
    private BudgetService Budget => Services.GetRequiredService<BudgetService>();
    private BackupService Backup => Services.GetRequiredService<BackupService>();

    [Fact]
    public void A_change_is_stored_and_announced_in_one_step()
    {
        var announced = 0m;
        State.Changed += () => announced = Stored.Kindergeld.Received;

        Kindergeld.ReceiveMonth();

        Assert.Equal(259m, announced); // stored by the time anyone hears of it
    }

    [Fact]
    public void A_change_the_domain_refuses_is_neither_stored_nor_announced()
    {
        var announced = false;
        State.Changed += () => announced = true;

        Assert.Throws<ArgumentOutOfRangeException>(() => Kindergeld.Configure(children: 0, endDigit: null));

        Assert.False(announced);
        Assert.Equal(1, Stored.Kindergeld.Children);
    }

    [Fact]
    public void An_edited_entry_is_updated_in_place()
    {
        Kindergeld.Book(null, EntryKind.Spending, Today, 30, "Kleidung", null);
        var entry = Kindergeld.Pot.Entries.Single();

        Kindergeld.Book(entry.Id, EntryKind.Spending, Today, 34.90m, "Kleidung", "Gummistiefel");

        Assert.Equal(entry with { Amount = 34.90m, Note = "Gummistiefel" }, Assert.Single(Stored.Kindergeld.Entries));
    }

    [Fact]
    public void An_edited_fixed_cost_that_a_sync_removed_meanwhile_is_saved_as_a_new_one()
    {
        Budget.SaveDeduction(null, "Handy", 19.99m, 15);
        var phone = Budget.Budget.Deductions.Single();
        State.Change(data => data.Budget.RemoveDeduction(phone.Id)); // what a sync does when the other phone deleted it

        Budget.SaveDeduction(phone.Id, "Handy", 24.99m, 15);

        var saved = Assert.Single(Stored.Budget.Deductions);
        Assert.Equal(("Handy", 24.99m, 15), (saved.Name, saved.Amount, saved.Day));
    }

    [Fact]
    public void The_rücklage_counts_from_today()
    {
        Budget.SetReserve(2300);

        Assert.Equal(new Reserve(2300, Today), Stored.Budget.Reserve);
    }

    [Fact]
    public async Task Loading_a_backup_onto_an_empty_device_asks_nothing()
    {
        var backup = new AppData();
        backup.Kindergeld.Receive(Today, 518);

        var result = await Backup.ImportAsync(DataStore.Serialize(backup), () => throw new InvalidOperationException("Asked."));

        Assert.Equal(ImportResult.Restored, result);
        Assert.Equal(518m, Stored.Kindergeld.Received);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_refused_before_asking_anything()
    {
        Kindergeld.ReceiveMonth();

        var result = await Backup.ImportAsync("""{"photos":[]}""", () => throw new InvalidOperationException("Asked."));

        Assert.Equal(ImportResult.NotABackup, result);
        Assert.Equal(259m, Stored.Kindergeld.Received);
    }
}
