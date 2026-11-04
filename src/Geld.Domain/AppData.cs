using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Domain.Sync;

namespace Geld.Domain;

/// <summary>
/// Everything on a phone: the synced pot and budget, and when the last backup was made. It composes the modules, and
/// none of them knows it. It is stored as one document, so the data and its sync state are always written together.
/// </summary>
public sealed class AppData
{
    /// <summary>How long a backup counts as recent; after that the app reminds the user to make one.</summary>
    public const int BackupReminderDays = 30;

    public AppData() : this(new SyncedData(), null)
    {
    }

    internal AppData(SyncedData synced, DateOnly? lastBackup) => (Synced, LastBackup) = (synced, lastBackup);

    public SyncedData Synced { get; }

    public KindergeldPot Kindergeld => Synced.Kindergeld;
    public Budget Budget => Synced.Budget;

    /// <summary>When the user last exported a backup; null if never.</summary>
    public DateOnly? LastBackup { get; private set; }

    /// <summary>
    /// True if nothing differs from a fresh start. It asks the sync's diffs because they know every field, so a field
    /// added later counts without being listed here. The Rücklage was once forgotten in a list like that.
    /// </summary>
    public bool IsEmpty =>
        KindergeldSync.Diff(new KindergeldPot(), Kindergeld).IsEmpty && BudgetSync.Diff(new Budget(), Budget).IsEmpty;

    /// <summary>
    /// The browser may delete its storage without warning, so the first backup is due as soon as there is data.
    /// </summary>
    public bool NeedsBackup(DateOnly today) =>
        !IsEmpty && (LastBackup is not { } last || today.DayNumber - last.DayNumber >= BackupReminderDays);

    public void MarkBackedUp(DateOnly today) => LastBackup = today;

    /// <summary>Replaces the data with a backup's (see <see cref="SyncedData.RestoreFrom"/> for the sync).</summary>
    public void RestoreFrom(AppData backup)
    {
        Synced.RestoreFrom(backup.Kindergeld, backup.Budget);
        LastBackup = backup.LastBackup;
    }
}
