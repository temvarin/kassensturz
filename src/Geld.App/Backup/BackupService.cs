using Geld.Domain;
using Geld.Domain.Storage;

namespace Geld.App.Backup;

public enum ImportResult
{
    Restored,

    /// <summary>Not a backup of this app, or of a version it doesn't know, or it breaks the app's rules.</summary>
    NotABackup,

    /// <summary>The user chose to keep the data on this device.</summary>
    Kept,
}

/// <summary>
/// Copies of the data outside the browser, which may delete its storage without warning (ADR 1): backup files, the
/// reminder to make one, and stored data this version couldn't read.
/// </summary>
public sealed class BackupService(AppState state, DataStore store, Browser browser, TimeProvider clock)
{
    /// <summary>null if never.</summary>
    public DateOnly? LastBackup => state.Data.LastBackup;

    /// <summary>Whether the data has a copy on the server too, which changes what a backup is for.</summary>
    public bool IsSynced => state.Data.Synced.State is not null;

    /// <summary>Whether to remind the user: there is data, and no backup in the last 30 days.</summary>
    public bool IsDue => state.Data.NeedsBackup(clock.Today());

    /// <summary>Hands a backup file to the share sheet; it counts as made only if the user saved it.</summary>
    public async Task ExportAsync()
    {
        var today = clock.Today();
        if (await browser.SaveFileAsync(FileName(today), DataStore.Backup(state.Data, today)))
            state.Change(data => data.MarkBackedUp(today));
    }

    /// <summary>Replaces the data on this device with a backup file's.</summary>
    /// <param name="confirmReplace">Asked only if there is data to lose; false keeps it.</param>
    public async Task<ImportResult> ImportAsync(string json, Func<Task<bool>> confirmReplace)
    {
        if (DataStore.TryRead(json) is not { } backup) return ImportResult.NotABackup;
        if (!state.Data.IsEmpty && !await confirmReplace()) return ImportResult.Kept;
        state.Change(data => data.RestoreFrom(backup));
        return ImportResult.Restored;
    }

    /// <summary>Stored data this version couldn't read, kept aside when the app started; null if there is none.</summary>
    public string? Unreadable => store.Unreadable;

    /// <summary>Exactly as it was stored: a version of the app that knows it can load the file as a backup.</summary>
    public async Task SaveUnreadableAsync()
    {
        if (store.Unreadable is { } unreadable) await browser.SaveFileAsync(FileName(clock.Today(), "nicht-lesbar-"), unreadable);
    }

    public void DiscardUnreadable()
    {
        store.DiscardUnreadable();
        state.Refresh(); // the banners go
    }

    private static string FileName(DateOnly today, string kind = "") => $"{AppInfo.Name.ToLowerInvariant()}-{kind}{today:yyyy-MM-dd}.json";
}
