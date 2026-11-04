using Geld.Domain.Sync;

namespace Geld.Domain.Contracts;

/// <summary>
/// Everything on a phone, as stored in localStorage (<c>geld.data</c>) and, without the sync state, in backup files.
/// Changing a shape here changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).
/// </summary>
public sealed record AppDataJson
{
    public KindergeldJson Kindergeld { get; init; } = new();
    public BudgetJson Budget { get; init; } = new();
    public DateOnly? LastBackup { get; init; }
    public SyncStateJson? Sync { get; init; }
    public SyncRequestJson? Unconfirmed { get; init; }

    public static AppDataJson From(AppData data) => new()
    {
        Kindergeld = KindergeldJson.From(data.Kindergeld),
        Budget = BudgetJson.From(data.Budget),
        LastBackup = data.LastBackup,
        Sync = data.Synced.State is { } sync ? SyncStateJson.From(sync) : null,
        Unconfirmed = data.Synced.Unconfirmed is { } unconfirmed ? SyncRequestJson.From(unconfirmed) : null,
    };

    /// <exception cref="ArgumentException">It breaks the domain's rules.</exception>
    public AppData ToDomain() =>
        new(new SyncedData(Kindergeld.ToDomain(), Budget.ToDomain(), Sync?.ToDomain(), Unconfirmed?.ToDomain()), LastBackup);
}

public sealed record StoredJson(int Schema, AppDataJson Data);
