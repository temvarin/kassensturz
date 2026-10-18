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
        // Throws for a missing item (JSON null) and for an id that appears twice.
        var changed = Changed.Select(i => valid(i ?? throw new ArgumentNullException(nameof(items)))).ToDictionary(i => i.Id);
        var removed = Removed.ToHashSet();
        IReadOnlyList<T> kept = [.. items.Where(i => !removed.Contains(i.Id)).Select(i => changed.Remove(i.Id, out var c) ? c : i)];
        return (kept, [.. Changed.Where(i => changed.ContainsKey(i.Id)).Select(i => changed[i.Id])]);
    }

    /// <summary>Both changes together; for an item in both, this one's.</summary>
    internal ItemChanges<T> Union(ItemChanges<T> other) =>
        new([.. Changed.UnionBy(other.Changed, i => i.Id)], [.. Removed.Union(other.Removed)]);

    /// <summary>The same items as they are now in <paramref name="items"/>: changed if still there, removed if not.</summary>
    internal ItemChanges<T> Now(IReadOnlyList<T> items)
    {
        var ids = Changed.Select(i => i.Id).Concat(Removed).ToHashSet();
        var present = items.Where(i => ids.Contains(i.Id)).ToList();
        return new(present, [.. ids.Except(present.Select(i => i.Id))]);
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

    internal KindergeldChanges Union(KindergeldChanges other) => new(Settings ?? other.Settings, Entries.Union(other.Entries));
}

/// <param name="NetSalary">null if unchanged.</param>
public sealed record BudgetChanges(decimal? NetSalary, ItemChanges<Deduction> Deductions, ItemChanges<PlannedSpending> Planned)
{
    public bool IsEmpty => NetSalary is null && Deductions.IsEmpty && Planned.IsEmpty;

    internal BudgetChanges Union(BudgetChanges other) =>
        new(NetSalary ?? other.NetSalary, Deductions.Union(other.Deductions), Planned.Union(other.Planned));
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
/// <param name="UserId">
/// The account the phone's data belongs to. The server refuses the request if someone else is signed in: the sign-in
/// cookie may have changed since the app last asked (another account signed in, in another tab). Optional only so that
/// requests of older apps, which don't send it, still reach the protocol check.
/// </param>
public sealed record SyncRequest(int Protocol, KindergeldChanges Kindergeld, BudgetChanges Budget, string? UserId = null)
{
    public const int CurrentProtocol = 2;

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
