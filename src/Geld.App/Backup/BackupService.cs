using Geld.Domain;
using Geld.Domain.Storage;

namespace Geld.App.Backup;

public enum ImportResult
{
    Restored,

    /// <summary>Not a backup of this app, from an unknown version, or against the app's rules.</summary>
    NotABackup,

    Kept,
}

/// <summary>
/// Copies of the data outside the browser, which may delete its storage without warning: backup files, the reminder
/// to make one, and stored data this version couldn't read.
/// </summary>
public sealed class BackupService(AppState state, DataStore store, Browser browser, TimeProvider clock)
{
    public DateOnly? LastBackup => state.Data.LastBackup;

    /// <summary>With a copy on the server, a backup is for something else, so the settings say so.</summary>
    public bool IsSynced => state.Data.Synced.State is not null;

    public bool IsDue => state.Data.NeedsBackup(clock.Today());

    /// <summary>A backup counts as made only if the user really saved the file.</summary>
    public async Task ExportAsync()
    {
        var today = clock.Today();
        if (await browser.SaveFileAsync(FileName(today), DataStore.Backup(state.Data, today)))
            state.Change(data => data.MarkBackedUp(today));
    }

    /// <param name="confirmReplace">Asked only if there is data to lose.</param>
    public async Task<ImportResult> ImportAsync(string json, Func<Task<bool>> confirmReplace)
    {
        if (DataStore.TryRead(json) is not { } backup) return ImportResult.NotABackup;
        if (!state.Data.IsEmpty && !await confirmReplace()) return ImportResult.Kept;
        state.Change(data => data.RestoreFrom(backup));
        return ImportResult.Restored;
    }

    public string? Unreadable => store.Unreadable;

    /// <summary>Saved exactly as it was stored, so a version that knows it can load the file as a backup.</summary>
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
