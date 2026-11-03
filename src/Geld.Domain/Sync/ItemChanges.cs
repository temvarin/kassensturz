using Geld.Domain.SharedKernel;

namespace Geld.Domain.Sync;

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
    /// back: the change that arrives last wins. The rules for the items are checked by the document they end up in.
    /// </summary>
    internal (IReadOnlyList<T> Kept, IReadOnlyList<T> Added) ApplyTo(IReadOnlyList<T> items)
    {
        // Throws for a missing item (JSON null) and for an id that appears twice.
        var changed = Changed.Select(i => i ?? throw new ArgumentNullException(nameof(items))).ToDictionary(i => i.Id);
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

