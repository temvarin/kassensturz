using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Domain.Sync;

namespace Geld.Domain;

/// <summary>
/// Everything on a phone: the user's data from every module, and what the device knows about its last sync and backup.
/// It composes the modules and knows them all; none of them knows it. The sync of the pot and the budget is the sync
/// module's (<see cref="SyncedData"/>); this adds the backup. Stored as one document (Contracts.AppDataJson), so the
/// data and its sync state are always written together; a backup is the same document without the sync state.
/// </summary>
public sealed class AppData
{
    /// <summary>How long a backup counts as recent; after that the app reminds the user to make one.</summary>
    public const int BackupReminderDays = 30;

    public AppData() : this(new SyncedData(), null)
    {
    }

    /// <summary>The data as it was saved.</summary>
    internal AppData(SyncedData synced, DateOnly? lastBackup) => (Synced, LastBackup) = (synced, lastBackup);

    /// <summary>The pot and the budget, with their sync.</summary>
    public SyncedData Synced { get; }

    public KindergeldPot Kindergeld => Synced.Kindergeld;
    public Budget Budget => Synced.Budget;

    /// <summary>When the user last exported a backup; null if never.</summary>
    public DateOnly? LastBackup { get; private set; }

    /// <summary>
    /// Whether the user has entered nothing: the data differs in nothing from a fresh start. Asked of the sync's diffs,
    /// which know every field, so a field added later counts without being listed here (the Rücklage once wasn't).
    /// </summary>
    public bool IsEmpty =>
        KindergeldSync.Diff(new(), Kindergeld).IsEmpty && BudgetSync.Diff(new(), Budget).IsEmpty;

    /// <summary>
    /// Whether data exists only in the browser's storage, which the browser may delete without warning.
    /// The first backup is due as soon as there is data.
    /// </summary>
    public bool NeedsBackup(DateOnly today) =>
        !IsEmpty && (LastBackup is not { } last || today.DayNumber - last.DayNumber >= BackupReminderDays);

    public void MarkBackedUp(DateOnly today) => LastBackup = today;

    /// <summary>Replaces the data with a backup; see <see cref="SyncedData.RestoreFrom"/> for what that means for the sync.</summary>
    public void RestoreFrom(AppData backup)
    {
        Synced.RestoreFrom(backup.Kindergeld, backup.Budget);
        LastBackup = backup.LastBackup;
    }
}
