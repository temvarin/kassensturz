using Geld.Domain.Budgeting;

namespace Geld.Domain.Contracts;

// The JSON of a budget: in localStorage, in backup files, in sync answers and in the database. Changing a shape here
// changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).

public sealed record DeductionJson(Guid Id, string Name, decimal Amount, int Day)
{
    public static DeductionJson From(Deduction d) => new(d.Id, d.Name, d.Amount, d.Day);

    public Deduction ToDomain() => new(Id, Name, Amount, Day);
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

    public static BudgetJson From(Budget budget) => new()
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
