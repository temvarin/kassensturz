using Geld.Domain.SharedKernel;

namespace Geld.Domain.Sync;

/// <summary>
/// The items of one list that were added, edited or removed since the last sync. Changed items travel whole ("entry X
/// is 50 €", never "add 5 €"), so applying the same changes twice gives the same result, and a request repeated after
/// a timeout does no harm.
/// </summary>
/// <param name="Changed">Added or edited.</param>
/// <param name="Removed">The ids of removed items.</param>
public sealed record ItemChanges<T>(IReadOnlyList<T> Changed, IReadOnlyList<Guid> Removed) where T : IItem
{
    public bool IsEmpty => Changed.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// Edited items replace the item with the same id in place. All other changed items are new, and the document decides
    /// where they go. An item edited on one device and removed on another comes back, because the change that arrives
    /// last wins.
    /// </summary>
    /// <exception cref="ArgumentException">An id appears twice among the changed items.</exception>
    internal (IReadOnlyList<T> Kept, IReadOnlyList<T> Added) ApplyTo(IReadOnlyList<T> items)
    {
        var edited = Changed.ToDictionary(i => i.Id);
        var removed = Removed.ToHashSet();
        List<T> kept = [.. items.Where(i => !removed.Contains(i.Id)).Select(i => edited.GetValueOrDefault(i.Id, i))];
        var keptIds = kept.Select(i => i.Id).ToHashSet();
        List<T> added = [.. Changed.Where(i => !keptIds.Contains(i.Id))];
        return (kept, added);
    }

    /// <summary>For an item in both, this one's version.</summary>
    internal ItemChanges<T> Union(ItemChanges<T> other) =>
        new([.. Changed.UnionBy(other.Changed, i => i.Id)], [.. Removed.Union(other.Removed)]);

    /// <summary>The same items as they are now: changed if still in <paramref name="items"/>, removed if not.</summary>
    internal ItemChanges<T> Now(IReadOnlyList<T> items)
    {
        var ids = Changed.Select(i => i.Id).Concat(Removed).ToHashSet();
        var present = items.Where(i => ids.Contains(i.Id)).ToList();
        return new(present, [.. ids.Except(present.Select(i => i.Id))]);
    }
}

public static class ItemChanges
{
    // Items are records, so Except compares them by value: an item counts as changed when any of its values did.
    public static ItemChanges<T> Between<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) where T : IItem =>
        new([.. after.Except(before)], [.. before.Select(i => i.Id).Except(after.Select(i => i.Id))]);
}

