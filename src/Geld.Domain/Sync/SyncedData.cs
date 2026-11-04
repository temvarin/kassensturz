using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;

namespace Geld.Domain.Sync;

/// <summary>
/// The synced documents on a phone (the shared pot and the user's own budget) and what the device knows about their
/// sync. One request carries the changes of both, for one account.
/// </summary>
public sealed class SyncedData
{
    private int restores;

    public SyncedData()
    {
    }

    internal SyncedData(KindergeldPot kindergeld, Budget budget, SyncState? state, SyncRequest? unconfirmed) =>
        (Kindergeld, Budget, State, Unconfirmed) = (kindergeld, budget, state, unconfirmed);

    public KindergeldPot Kindergeld { get; private set; } = new();
    public Budget Budget { get; private set; } = new();

    /// <summary>The last sync, with the documents as the server confirmed them; null until the first sync.</summary>
    public SyncState? State { get; private set; }

    /// <summary>
    /// Changes of syncs whose answer never came, so the server may or may not have saved them. Until a sync succeeds,
    /// the items they touched are sent again as they are now. A diff against the base alone would miss an entry that a
    /// lost sync added on the server and that was deleted here afterwards.
    /// </summary>
    public SyncRequest? Unconfirmed { get; private set; }

    /// <summary>Before the first sync, all data counts as unsynced.</summary>
    public bool HasUnsyncedChanges
    {
        get
        {
            var (kindergeld, budget) = Changes();
            return !kindergeld.IsEmpty || !budget.IsEmpty;
        }
    }

    /// <exception cref="InvalidOperationException">The data here was synced with another account.</exception>
    public PendingSync StartSync(string userId)
    {
        if (State is { } state && state.UserId != userId)
            throw new InvalidOperationException("The data on this device belongs to another account.");
        var (kindergeld, budget) = Changes();
        return new(new SyncRequest(kindergeld, budget, userId), Kindergeld.Copy(), Budget.Copy()) { Restores = restores };
    }

    public void CompleteSync(PendingSync sent, SyncResponse answer, DateTimeOffset now)
    {
        if (sent.Restores != restores)
        {
            // A backup was restored while the request was under way. The answer predates the backup, so it is not taken
            // over, and the next sync adds the backup like a first sync, as RestoreFrom promises.
            (State, Unconfirmed) = (new SyncState(sent.Request.UserId, now, new KindergeldPot(), new Budget()), null);
            return;
        }

        // The answer lacks what changed here while the request was under way. That goes on top of it again, like a
        // git rebase, and waits for the next sync.
        var kindergeld = KindergeldSync.Apply(answer.Kindergeld, KindergeldSync.Diff(sent.Kindergeld, Kindergeld));
        var budget = BudgetSync.Apply(answer.Budget, BudgetSync.Diff(sent.Budget, Budget));
        (Kindergeld, Budget, Unconfirmed) = (kindergeld, budget, null);
        State = new SyncState(sent.Request.UserId, now, answer.Kindergeld, answer.Budget);
    }

    public void SyncFailed(PendingSync sent)
    {
        if (sent.Restores != restores) return; // sent before a backup was restored, which forgot it (RestoreFrom)
        Unconfirmed = Unconfirmed is null
            ? sent.Request
            : Unconfirmed with
            {
                Kindergeld = Unconfirmed.Kindergeld.Union(sent.Request.Kindergeld),
                Budget = Unconfirmed.Budget.Union(sent.Request.Budget),
            };
    }

    /// <summary>
    /// Replaces the documents with a backup's. A synced device stays with its account but forgets what the server had,
    /// so the next sync adds the backup like a first sync instead of removing everything the backup lacks. That includes
    /// the unanswered changes, whose items would otherwise go out as removed.
    /// </summary>
    public void RestoreFrom(KindergeldPot kindergeld, Budget budget)
    {
        (Kindergeld, Budget, Unconfirmed) = (kindergeld, budget, null);
        if (State is { } state) State = state with { Kindergeld = new KindergeldPot(), Budget = new Budget() };
        restores++;
    }

    private (KindergeldChanges Kindergeld, BudgetChanges Budget) Changes()
    {
        var kindergeld = KindergeldSync.Diff(State?.Kindergeld ?? new KindergeldPot(), Kindergeld);
        var budget = BudgetSync.Diff(State?.Budget ?? new Budget(), Budget);
        if (Unconfirmed is null) return (kindergeld, budget);
        return (kindergeld.Union(KindergeldSync.Now(Kindergeld, Unconfirmed.Kindergeld)),
            budget.Union(BudgetSync.Now(Budget, Unconfirmed.Budget)));
    }
}
