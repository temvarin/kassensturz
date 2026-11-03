using Geld.Domain.SharedKernel;
using Geld.Domain.Sync;

namespace Geld.Domain.Contracts;

// The JSON of POST /api/sync, and of the unanswered changes and the sync state a phone keeps in localStorage. Changing
// a shape here changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).
//
// The IsEmpty properties are a wart: computed properties that the first versions wrote along by accident. Nothing
// reads them, but they stay in the JSON until the format is changed on purpose.

public sealed record ItemChangesJson<T>(IReadOnlyList<T> Changed, IReadOnlyList<Guid> Removed)
{
    public bool IsEmpty => Changed.Count == 0 && Removed.Count == 0;

    public ItemChanges<TItem> ToDomain<TItem>(Func<T, TItem> domain) where TItem : IItem =>
        new([.. Changed.Select(item => domain(item ?? throw new ArgumentException("A changed item is missing (null).")))], Removed);
}

public static class ItemChangesJson
{
    public static ItemChangesJson<T> From<TItem, T>(ItemChanges<TItem> changes, Func<TItem, T> json) where TItem : IItem =>
        new([.. changes.Changed.Select(json)], changes.Removed);
}

public sealed record KindergeldSettingsJson(int Children, int? EndDigit);

public sealed record KindergeldChangesJson(KindergeldSettingsJson? Settings, ItemChangesJson<EntryJson> Entries)
{
    public bool IsEmpty => Settings is null && Entries.IsEmpty;

    public static KindergeldChangesJson From(KindergeldChanges changes) => new(
        changes.Settings is { } s ? new(s.Children, s.EndDigit) : null,
        ItemChangesJson.From(changes.Entries, EntryJson.From));

    public KindergeldChanges ToDomain() => new(
        Settings is { } s ? new(s.Children, s.EndDigit) : null,
        Entries.ToDomain(e => e.ToDomain()));
}

/// <param name="Reserve">null: no Rücklage.</param>
public sealed record BudgetSettingsJson(decimal NetSalary, ReserveJson? Reserve);

/// <param name="Settings">
/// null if unchanged. Last and optional only so that requests of protocol 2 and older, which have none, still reach the
/// protocol check.
/// </param>
public sealed record BudgetChangesJson(
    ItemChangesJson<DeductionJson> Deductions, ItemChangesJson<PlannedSpendingJson> Planned, BudgetSettingsJson? Settings = null)
{
    public bool IsEmpty => Settings is null && Deductions.IsEmpty && Planned.IsEmpty;

    public static BudgetChangesJson From(BudgetChanges changes) => new(
        ItemChangesJson.From(changes.Deductions, DeductionJson.From),
        ItemChangesJson.From(changes.Planned, PlannedSpendingJson.From),
        changes.Settings is { } s ? new(s.NetSalary, s.Reserve is { } r ? ReserveJson.From(r) : null) : null);

    public BudgetChanges ToDomain() => new(
        Settings is { } s ? new(s.NetSalary, s.Reserve?.ToDomain()) : null,
        Deductions.ToDomain(d => d.ToDomain()),
        Planned.ToDomain(p => p.ToDomain()));
}

/// <summary>The body of <c>POST /api/sync</c>, and the unanswered changes a phone keeps (<see cref="SyncedData.Unconfirmed"/>).</summary>
/// <param name="Protocol">
/// The shape of these requests and of the documents. Installed apps keep running an old version until they are
/// restarted, and an old version might drop what it doesn't know when it sends data back; so the server refuses
/// requests of another protocol. Raise <see cref="CurrentProtocol"/> whenever that shape changes in a way older apps
/// can't take part in. That includes a new field in an item or in the settings: changed items and settings travel
/// whole, so an older app would send them without it and clear it. Only a new optional part of the request, which
/// older apps never send, doesn't count. Protocol 3 (2026-11-03): the budget's salary and Rücklage became one settings
/// record.
/// </param>
/// <param name="UserId">
/// The account the phone's data belongs to (see <see cref="SyncRequest.UserId"/>). Optional only so that requests of
/// older apps, which don't send it, still reach the protocol check.
/// </param>
public sealed record SyncRequestJson(int Protocol, KindergeldChangesJson Kindergeld, BudgetChangesJson Budget, string? UserId = null)
{
    public const int CurrentProtocol = 3;

    public bool IsEmpty => Kindergeld.IsEmpty && Budget.IsEmpty;

    public static SyncRequestJson From(SyncRequest request) => new(
        CurrentProtocol, KindergeldChangesJson.From(request.Kindergeld), BudgetChangesJson.From(request.Budget), request.UserId);

    /// <exception cref="ArgumentException">It names no account, or an item is missing.</exception>
    public SyncRequest ToDomain() => new(Kindergeld.ToDomain(), Budget.ToDomain(),
        UserId ?? throw new ArgumentException("The request names no account.", nameof(UserId)));
}

/// <summary>The answer to <c>POST /api/sync</c>.</summary>
public sealed record SyncResponseJson(KindergeldJson Kindergeld, BudgetJson Budget)
{
    public static SyncResponseJson From(SyncResponse response) =>
        new(KindergeldJson.From(response.Kindergeld), BudgetJson.From(response.Budget));

    /// <exception cref="ArgumentException">It breaks the domain's rules.</exception>
    public SyncResponse ToDomain() => new(Kindergeld.ToDomain(), Budget.ToDomain());
}

public sealed record SyncStateJson(string UserId, DateTimeOffset LastSync, KindergeldJson Kindergeld, BudgetJson Budget)
{
    public static SyncStateJson From(SyncState state) =>
        new(state.UserId, state.LastSync, KindergeldJson.From(state.Kindergeld), BudgetJson.From(state.Budget));

    public SyncState ToDomain() => new(UserId, LastSync, Kindergeld.ToDomain(), Budget.ToDomain());
}
