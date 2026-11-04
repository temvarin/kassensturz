using Geld.Domain.Budgeting;

namespace Geld.Domain.Contracts;

// A budget's JSON, the same on the phone, in backup files, in sync answers and in the database. Changing a shape here
// changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).

/// <param name="Day">
/// The day of the month. A cost every few weeks writes the day of its first date, because every reader of older
/// versions requires the field.
/// </param>
/// <param name="EveryWeeks">null for a cost every month. Added on 2026-11-04 (protocol 4).</param>
/// <param name="First">The first debit of a cost every few weeks.</param>
public sealed record DeductionJson(Guid Id, string Name, decimal Amount, int Day, int? EveryWeeks = null, DateOnly? First = null)
{
    public static DeductionJson From(Deduction d) => d.Schedule switch
    {
        MonthlyOn monthly => new(d.Id, d.Name, d.Amount, monthly.Day),
        EveryWeeks every => new(d.Id, d.Name, d.Amount, every.First.Day, every.Weeks, every.First),
        _ => throw new ArgumentOutOfRangeException(nameof(d), d.Schedule, "Unknown schedule."),
    };

    /// <exception cref="ArgumentException">A cost every few weeks has no first date.</exception>
    public Deduction ToDomain() => new(Id, Name, Amount, EveryWeeks is { } weeks
        ? new EveryWeeks(weeks, First ?? throw new ArgumentException("A cost every few weeks needs its first date.", nameof(First)))
        : new MonthlyOn(Day));
}

public sealed record PlannedSpendingJson(Guid Id, int Year, int Month, string Name, decimal Amount)
{
    public static PlannedSpendingJson From(PlannedSpending p) => new(p.Id, p.Year, p.Month, p.Name, p.Amount);

    public PlannedSpending ToDomain() => new(Id, Year, Month, Name, Amount);
}

public sealed record ReserveJson(decimal Amount, DateOnly AsOf)
{
    public static ReserveJson From(Reserve reserve) => new(reserve.Amount, reserve.AsOf);

    public Reserve ToDomain() => new(Amount, AsOf);
}

public sealed record BudgetJson
{
    public decimal NetSalary { get; init; }
    public IReadOnlyList<DeductionJson> Deductions { get; init; } = [];
    public IReadOnlyList<PlannedSpendingJson> Planned { get; init; } = [];

    /// <summary>Added on 2026-10-25; budgets saved before have none.</summary>
    public ReserveJson? Reserve { get; init; }

    public static BudgetJson From(IReadOnlyBudget budget) => new()
    {
        NetSalary = budget.NetSalary,
        Deductions = [.. budget.Deductions.Select(DeductionJson.From)],
        Planned = [.. budget.Planned.Select(PlannedSpendingJson.From)],
        Reserve = budget.Reserve is { } reserve ? ReserveJson.From(reserve) : null,
    };

    /// <exception cref="ArgumentException">It breaks the budget's rules.</exception>
    public Budget ToDomain() => new(NetSalary,
        Deductions.Select(d => Json.Item(d).ToDomain()), Planned.Select(p => Json.Item(p).ToDomain()), Reserve?.ToDomain());
}
