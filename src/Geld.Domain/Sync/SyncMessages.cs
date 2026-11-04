using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;

namespace Geld.Domain.Sync;

public sealed record KindergeldSettings(int Children, int? EndDigit);

/// <param name="Settings">null if unchanged.</param>
public sealed record KindergeldChanges(KindergeldSettings? Settings, ItemChanges<Entry> Entries)
{
    public bool IsEmpty => Settings is null && Entries.IsEmpty;

    internal KindergeldChanges Union(KindergeldChanges other) => new(Settings ?? other.Settings, Entries.Union(other.Entries));
}

/// <summary>
/// Synced as one record, so of two devices that change the settings, the one the server hears from last wins. A new
/// setting needs a new protocol, because an older app would send the record without it and clear it.
/// </summary>
/// <param name="Reserve">null means no Rücklage.</param>
public sealed record BudgetSettings(decimal NetSalary, Reserve? Reserve);

/// <param name="Settings">null if unchanged.</param>
public sealed record BudgetChanges(BudgetSettings? Settings, ItemChanges<Deduction> Deductions, ItemChanges<PlannedSpending> Planned)
{
    public bool IsEmpty => Settings is null && Deductions.IsEmpty && Planned.IsEmpty;

    internal BudgetChanges Union(BudgetChanges other) =>
        new(Settings ?? other.Settings, Deductions.Union(other.Deductions), Planned.Union(other.Planned));
}

/// <summary>What changed on this phone since its last sync, for the shared pot and the user's own budget.</summary>
/// <param name="UserId">
/// The account the data belongs to. The server refuses the request if someone else is signed in by now, for example
/// in another tab.
/// </param>
public sealed record SyncRequest(KindergeldChanges Kindergeld, BudgetChanges Budget, string UserId);

public sealed record SyncResponse(IReadOnlyKindergeldPot Kindergeld, IReadOnlyBudget Budget);

/// <summary>
/// The last sync. The documents as the server confirmed them are the base, and the next sync sends what changed since,
/// much like a git branch and the commit it started from.
/// </summary>
/// <param name="UserId">The account the data belongs to. It is never synced with another one.</param>
public sealed record SyncState(string UserId, DateTimeOffset LastSync, IReadOnlyKindergeldPot Kindergeld, IReadOnlyBudget Budget);

/// <summary>A sync under way: the request, and the documents as they were when it was sent.</summary>
public sealed record PendingSync(SyncRequest Request, IReadOnlyKindergeldPot Kindergeld, IReadOnlyBudget Budget)
{
    /// <summary>How many backups had been restored when the sync started.</summary>
    internal int Restores { get; init; }
}
