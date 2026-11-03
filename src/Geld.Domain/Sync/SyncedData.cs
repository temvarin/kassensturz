using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;

namespace Geld.Domain.Sync;

/// <summary>
/// The synced documents on a phone, the pot and the user's budget, with what the device knows about their sync: one
/// sync sends the changes of both, for one account. Each document is synced by its own
/// <see cref="Replica{TDocument, TChanges, TAdapter}"/>; this pairs them and keeps the account.
/// </summary>
public sealed class SyncedData
{
    private readonly Replica<KindergeldPot, KindergeldChanges, KindergeldSync> kindergeld;
    private readonly Replica<Budget, BudgetChanges, BudgetSync> budget;
    private (string UserId, DateTimeOffset LastSync)? account;

    // The account the unanswered changes were sent for. Only kept to be stored again as it was: the changes are sent
    // again only for the account the data belongs to.
    private string? unconfirmedFor;

    public SyncedData() : this(new(), new(), null, null)
    {
    }

    /// <summary>The documents as they were stored, with their last sync and their unanswered changes.</summary>
    internal SyncedData(KindergeldPot kindergeld, Budget budget, SyncState? state, SyncRequest? unconfirmed)
    {
        this.kindergeld = new(kindergeld, state?.Kindergeld, unconfirmed?.Kindergeld);
        this.budget = new(budget, state?.Budget, unconfirmed?.Budget);
        account = state is null ? null : (state.UserId, state.LastSync);
        unconfirmedFor = unconfirmed?.UserId;
    }

    public KindergeldPot Kindergeld => kindergeld.Current;
    public Budget Budget => budget.Current;

    /// <summary>The last sync and the documents as the server confirmed them then; null until the first sync.</summary>
    public SyncState? State => account is (var userId, var lastSync)
        ? new(userId, lastSync, kindergeld.Confirmed!, budget.Confirmed!) // confirmed together with the account
        : null;

    /// <summary>The changes of syncs whose answer never came; null if there are none.</summary>
    public SyncRequest? Unconfirmed => unconfirmedFor is { } userId
        ? new(kindergeld.Unconfirmed!, budget.Unconfirmed!, userId) // both are set and cleared together
        : null;

    /// <summary>Whether something changed since the last sync. Before the first sync, all data counts as a change.</summary>
    public bool HasUnsyncedChanges => !(KindergeldSync.IsEmpty(kindergeld.Changes()) && BudgetSync.IsEmpty(budget.Changes()));

    /// <summary>The request for a sync: what changed since the last one (on a first sync, everything).</summary>
    /// <exception cref="InvalidOperationException">The data here was synced with another account.</exception>
    public PendingSync StartSync(string userId)
    {
        if (account is (var syncedWith, _) && syncedWith != userId)
            throw new InvalidOperationException("The data on this device belongs to another account.");
        return new(new SyncRequest(kindergeld.Changes(), budget.Changes(), userId), kindergeld.Snapshot(), budget.Snapshot());
    }

    /// <summary>Takes the server's answer; the changes made here while the request was under way wait for the next sync.</summary>
    public void CompleteSync(PendingSync sent, SyncResponse answer, DateTimeOffset now)
    {
        kindergeld.Complete(sent.Kindergeld, answer.Kindergeld);
        budget.Complete(sent.Budget, answer.Budget);
        (account, unconfirmedFor) = ((sent.Request.UserId, now), null);
    }

    /// <summary>No answer came: the server may or may not have saved <paramref name="sent"/>.</summary>
    public void SyncFailed(PendingSync sent)
    {
        kindergeld.Failed(sent.Request.Kindergeld);
        budget.Failed(sent.Request.Budget);
        unconfirmedFor ??= sent.Request.UserId;
    }

    /// <summary>
    /// Replaces the documents with a backup's. A device that syncs stays with its account, but forgets what the server
    /// had: the next sync then adds the backup to the shared data like a first sync.
    /// </summary>
    public void RestoreFrom(KindergeldPot backupKindergeld, Budget backupBudget)
    {
        kindergeld.Restore(backupKindergeld);
        budget.Restore(backupBudget);
    }
}
