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
/// The budget's settings, synced as one: of two phones that change them, the change the server receives last wins
/// whole. A new setting is one more property here, and a new protocol (Contracts.SyncRequestJson): an older app would
/// send the settings without it, and so clear it.
/// </summary>
/// <param name="Reserve">null: no Rücklage.</param>
public sealed record BudgetSettings(decimal NetSalary, Reserve? Reserve);

/// <param name="Settings">null if unchanged.</param>
public sealed record BudgetChanges(BudgetSettings? Settings, ItemChanges<Deduction> Deductions, ItemChanges<PlannedSpending> Planned)
{
    public bool IsEmpty => Settings is null && Deductions.IsEmpty && Planned.IsEmpty;

    internal BudgetChanges Union(BudgetChanges other) =>
        new(Settings ?? other.Settings, Deductions.Union(other.Deductions), Planned.Union(other.Planned));
}

/// <summary>
/// What changed on this phone since its last sync: the server applies it to the shared pot and to the signed-in
/// user's own budget, and answers with both as they are then. Sent as Contracts.SyncRequestJson.
/// </summary>
/// <param name="UserId">
/// The account the phone's data belongs to. The server refuses the request if someone else is signed in: the sign-in
/// cookie may have changed since the app last asked (another account signed in, in another tab).
/// </param>
public sealed record SyncRequest(KindergeldChanges Kindergeld, BudgetChanges Budget, string UserId)
{
    public bool IsEmpty => Kindergeld.IsEmpty && Budget.IsEmpty;
}

/// <summary>The answer to a <see cref="SyncRequest"/>: the documents as the server now has them, all changes included.</summary>
public sealed record SyncResponse(KindergeldPot Kindergeld, Budget Budget);

/// <summary>
/// What a device knows about its last sync. The data as the server confirmed it then is the base: what changed on the
/// device since is what the next sync sends, much like a git branch and the commit it started from.
/// </summary>
/// <param name="UserId">The account the data belongs to; it is never synced with another one.</param>
public sealed record SyncState(string UserId, DateTimeOffset LastSync, KindergeldPot Kindergeld, Budget Budget);

/// <summary>A sync under way: the request, and the data as it was when the request was made.</summary>
public sealed record PendingSync(SyncRequest Request, KindergeldPot Kindergeld, Budget Budget);
