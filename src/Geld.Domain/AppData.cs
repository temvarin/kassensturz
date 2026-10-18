using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>
/// Everything the user entered, and what the device knows about its last sync. Stored as one document, so the data and
/// its sync state are always written together; a backup is the same document without the sync state. Persisted by
/// <see cref="DataStore"/>.
/// </summary>
public sealed class AppData
{
    /// <summary>How long a backup counts as recent; after that the app reminds the user to make one.</summary>
    public const int BackupReminderDays = 30;

    [JsonInclude] public KindergeldPot Kindergeld { get; internal set; } = new();
    [JsonInclude] public Budget Budget { get; internal set; } = new();

    /// <summary>When the user last exported a backup; null if never.</summary>
    [JsonInclude] public DateOnly? LastBackup { get; internal set; }

    /// <summary>null until the first sync.</summary>
    [JsonInclude] public SyncState? Sync { get; internal set; }

    /// <summary>
    /// The changes of syncs whose answer never came (a timeout, a lost connection): the server may or may not have
    /// saved them. Until a sync succeeds, the items they touched are sent again as they are now, so that whatever the
    /// server did, it ends up with this device's data; diffing against the last confirmed state alone would miss, for
    /// example, an entry deleted after the lost sync added it on the server. null if there are none.
    /// </summary>
    [JsonInclude] public SyncRequest? Unconfirmed { get; internal set; }

    [JsonIgnore]
    public bool IsEmpty => Kindergeld.Entries.Count == 0 && Budget.NetSalary == 0
        && Budget.Deductions.Count == 0 && Budget.Planned.Count == 0;

    /// <summary>Whether something changed since the last sync. Before the first sync, all data counts as a change.</summary>
    [JsonIgnore] public bool HasUnsyncedChanges => Changes() is var (kindergeld, budget) && !(kindergeld.IsEmpty && budget.IsEmpty);

    /// <summary>
    /// Whether data exists only in the browser's storage, which the browser may delete without warning.
    /// The first backup is due as soon as there is data.
    /// </summary>
    public bool NeedsBackup(DateOnly today) =>
        !IsEmpty && (LastBackup is not { } last || today.DayNumber - last.DayNumber >= BackupReminderDays);

    public void MarkBackedUp(DateOnly today) => LastBackup = today;

    /// <summary>
    /// Replaces the data with a backup. A device that syncs stays with its account, but forgets what the server had:
    /// the next sync then adds the backup to the shared data like a first sync, rather than removing everything the
    /// backup lacks.
    /// </summary>
    public void RestoreFrom(AppData backup)
    {
        (Kindergeld, Budget, LastBackup) = (backup.Kindergeld, backup.Budget, backup.LastBackup);
        if (Sync is { } sync) Sync = sync with { Kindergeld = new(), Budget = new() };
    }

    /// <summary>The request for a sync: what changed since the last one (on a first sync, everything).</summary>
    /// <exception cref="InvalidOperationException">The data here was synced with another account.</exception>
    public PendingSync StartSync(string userId)
    {
        if (Sync is { } sync && sync.UserId != userId)
            throw new InvalidOperationException("The data on this device belongs to another account.");
        var (kindergeld, budget) = Changes();
        return new(new SyncRequest(SyncRequest.CurrentProtocol, kindergeld, budget, userId), Copy(Kindergeld), Copy(Budget));
    }

    /// <summary>
    /// Takes the server's answer. It has every change the server received, this device's included, but not the changes
    /// made here while the request was under way: those are applied on top of it again, and wait for the next sync.
    /// </summary>
    public void CompleteSync(PendingSync sent, SyncResponse answer, DateTimeOffset now)
    {
        var kindergeld = Copy(answer.Kindergeld);
        kindergeld.Apply(Kindergeld.ChangesSince(sent.Kindergeld));
        var budget = Copy(answer.Budget);
        budget.Apply(Budget.ChangesSince(sent.Budget));
        (Kindergeld, Budget, Unconfirmed) = (kindergeld, budget, null);
        Sync = new SyncState(sent.Request.UserId!, now, answer.Kindergeld, answer.Budget);
    }

    /// <summary>No answer came: the server may or may not have saved <paramref name="sent"/>.</summary>
    public void SyncFailed(PendingSync sent) => Unconfirmed = Unconfirmed is { } earlier
        ? earlier with { Kindergeld = earlier.Kindergeld.Union(sent.Request.Kindergeld), Budget = earlier.Budget.Union(sent.Request.Budget) }
        : sent.Request;

    private (KindergeldChanges, BudgetChanges) Changes()
    {
        var kindergeld = Kindergeld.ChangesSince(Sync?.Kindergeld ?? new());
        var budget = Budget.ChangesSince(Sync?.Budget ?? new());
        return Unconfirmed is { } unconfirmed
            ? (kindergeld.Union(Kindergeld.Now(unconfirmed.Kindergeld)), budget.Union(Budget.Now(unconfirmed.Budget)))
            : (kindergeld, budget);
    }

    private static T Copy<T>(T value) where T : class
    {
        var type = ApiJson.Default.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException(typeof(T).Name);
        return (T)JsonSerializer.Deserialize(JsonSerializer.Serialize(value, type), type)!;
    }
}
