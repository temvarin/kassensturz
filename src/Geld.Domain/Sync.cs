namespace Geld.Domain;

/// <summary>
/// The items of one list that a phone added, edited or removed since its last sync. Each changed item travels whole, as
/// it is now ("entry X is 50 €", never "add 5 €"), so receiving the same changes twice gives the same result: a request
/// repeated after a timeout does no harm.
/// </summary>
/// <param name="Changed">Added or edited.</param>
/// <param name="Removed">The ids of removed items.</param>
public sealed record ItemChanges<T>(IReadOnlyList<T> Changed, IReadOnlyList<Guid> Removed) where T : IItem
{
    public bool IsEmpty => Changed.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// Edited items replace the item with the same id where it is; the others are new, and the caller puts them in
    /// their place. Removing an item that isn't there does nothing, and an edited item that was removed meanwhile comes
    /// back: the change that arrives last wins.
    /// </summary>
    /// <param name="valid">Checks an item with the rules that apply when it is entered; throws if it breaks them.</param>
    internal (IReadOnlyList<T> Kept, IReadOnlyList<T> Added) ApplyTo(IReadOnlyList<T> items, Func<T, T> valid)
    {
        var changed = Changed.Select(valid).ToDictionary(i => i.Id); // throws for an id that appears twice
        var removed = Removed.ToHashSet();
        IReadOnlyList<T> kept = [.. items.Where(i => !removed.Contains(i.Id)).Select(i => changed.Remove(i.Id, out var c) ? c : i)];
        return (kept, [.. Changed.Where(i => changed.ContainsKey(i.Id)).Select(i => changed[i.Id])]);
    }
}

public static class ItemChanges
{
    public static ItemChanges<T> Between<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) where T : IItem =>
        new([.. after.Except(before)], [.. before.Select(i => i.Id).Except(after.Select(i => i.Id))]);
}

public sealed record KindergeldSettings(int Children, int? EndDigit);

/// <param name="Settings">null if unchanged.</param>
public sealed record KindergeldChanges(KindergeldSettings? Settings, ItemChanges<Entry> Entries)
{
    public bool IsEmpty => Settings is null && Entries.IsEmpty;
}

/// <param name="NetSalary">null if unchanged.</param>
public sealed record BudgetChanges(decimal? NetSalary, ItemChanges<Deduction> Deductions, ItemChanges<PlannedSpending> Planned)
{
    public bool IsEmpty => NetSalary is null && Deductions.IsEmpty && Planned.IsEmpty;
}

/// <summary>
/// The body of <c>POST /api/sync</c>: what changed on this phone since its last sync. The server applies it to the
/// shared pot and to the signed-in user's own budget, and answers with both as they are then.
/// </summary>
/// <param name="Protocol">
/// The shape of these requests and of the documents. Installed apps keep running an old version until they are
/// restarted, and an old version would drop what it doesn't know when it sends data back; so the server refuses
/// requests of another protocol. Raise <see cref="CurrentProtocol"/> whenever that shape changes.
/// </param>
public sealed record SyncRequest(int Protocol, KindergeldChanges Kindergeld, BudgetChanges Budget)
{
    public const int CurrentProtocol = 1;

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
