namespace Geld.Domain.Sync;

/// <summary>
/// One synced document on this device: as it is here, as the server last confirmed it (the base, much like the commit a
/// git branch started from), and the changes of syncs whose answer never came. This is the sync's algorithm for one
/// document, and it knows the document only through its adapter.
/// </summary>
internal sealed class Replica<TDocument, TChanges, TAdapter>(TDocument current, TDocument? confirmed, TChanges? unconfirmed)
    where TDocument : class
    where TChanges : class
    where TAdapter : ISyncAdapter<TDocument, TChanges>
{
    public TDocument Current { get; private set; } = current;

    /// <summary>The document as the server last confirmed it; null until the first sync.</summary>
    public TDocument? Confirmed { get; private set; } = confirmed;

    /// <summary>
    /// The changes of syncs whose answer never came: the server may or may not have saved them. Until a sync succeeds,
    /// the items they touched are sent again as they are now, so that whatever the server did, it ends up with this
    /// device's data. Diffing against the base alone would miss, for example, an entry deleted after the lost sync added
    /// it on the server. null if there are none.
    /// </summary>
    public TChanges? Unconfirmed { get; private set; } = unconfirmed;

    /// <summary>What the next sync sends: what changed since the base (before the first sync, everything).</summary>
    public TChanges Changes()
    {
        var changes = TAdapter.Diff(Confirmed ?? TAdapter.Empty(), Current);
        return Unconfirmed is { } touched ? TAdapter.Union(changes, TAdapter.Now(Current, touched)) : changes;
    }

    /// <summary>The document as it is when a sync starts, which <see cref="Complete"/> needs back.</summary>
    public TDocument Snapshot() => TAdapter.Copy(Current);

    /// <summary>
    /// Takes the server's answer. It has every change the server received, this device's included, but not the changes
    /// made here since <paramref name="sent"/>: those are applied on top of it again, and wait for the next sync.
    /// </summary>
    public void Complete(TDocument sent, TDocument answer)
    {
        Current = TAdapter.Apply(answer, TAdapter.Diff(sent, Current));
        (Confirmed, Unconfirmed) = (answer, null);
    }

    /// <summary>No answer came: the server may or may not have saved <paramref name="sent"/>.</summary>
    public void Failed(TChanges sent) => Unconfirmed = Unconfirmed is { } earlier ? TAdapter.Union(earlier, sent) : sent;

    /// <summary>
    /// Replaces the document with a backup's. A synced device forgets what the server had: the next sync then adds the
    /// backup to the server's document like a first sync, rather than removing everything the backup lacks.
    /// </summary>
    public void Restore(TDocument backup)
    {
        Current = backup;
        if (Confirmed is not null) Confirmed = TAdapter.Empty();
    }
}
