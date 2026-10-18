using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>A fixed cost taken from the salary every month, e.g. rent or a subscription.</summary>
/// <param name="Day">Day of the month it is debited, 1–31; in shorter months the last day.</param>
public sealed record Deduction(Guid Id, string Name, decimal Amount, int Day) : IItem;

/// <summary>A spending the user plans for one particular month, e.g. make-up in October.</summary>
public sealed record PlannedSpending(Guid Id, int Year, int Month, string Name, decimal Amount) : IItem;

/// <param name="Date">When it is debited in this month.</param>
/// <param name="Done">Debited already: the date is today or earlier.</param>
public sealed record DeductionLine(Deduction Deduction, DateOnly Date, bool Done);

/// <summary>One month of the budget: the salary, what is taken from it, and what is left.</summary>
public sealed record MonthOverview(
    int Year, int Month, decimal Salary, IReadOnlyList<DeductionLine> Deductions, IReadOnlyList<PlannedSpending> Planned)
{
    public decimal FixedTotal => Deductions.Sum(d => d.Deduction.Amount);
    public decimal PlannedTotal => Planned.Sum(p => p.Amount);

    /// <summary>Freely available after fixed costs and planned spendings; negative when overplanned.</summary>
    public decimal Available => Salary - FixedTotal - PlannedTotal;

    /// <summary>Fixed costs not yet debited this month.</summary>
    public decimal StillToPay => Deductions.Where(d => !d.Done).Sum(d => d.Deduction.Amount);
}

/// <summary>
/// The monthly budget: one net salary, fixed costs that recur every month, and spendings planned for single months.
/// There is no history: a changed salary or rent applies to every month, which is why only the current and
/// later months are shown. Persisted as part of <see cref="AppData"/>.
/// </summary>
/// <remarks>State changes only through its methods; the internal setters exist for deserialization.</remarks>
public sealed class Budget
{
    [JsonInclude] public decimal NetSalary { get; internal set; }

    /// <summary>In the order they are debited.</summary>
    [JsonInclude] public IReadOnlyList<Deduction> Deductions { get; internal set; } = [];

    /// <summary>In the order they were added.</summary>
    [JsonInclude] public IReadOnlyList<PlannedSpending> Planned { get; internal set; } = [];

    public void SetSalary(decimal netSalary)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(netSalary);
        NetSalary = netSalary;
    }

    public Deduction AddDeduction(string name, decimal amount, int day)
    {
        var deduction = Valid(new Deduction(Guid.NewGuid(), name, amount, day));
        Deductions = Sorted([.. Deductions, deduction]);
        return deduction;
    }

    public void UpdateDeduction(Deduction deduction) => Deductions = Sorted(Deductions.Replacing(Valid(deduction)));

    public void RemoveDeduction(Guid id) => Deductions = Deductions.Without(id);

    public PlannedSpending Plan(int year, int month, string name, decimal amount)
    {
        var planned = Valid(new PlannedSpending(Guid.NewGuid(), year, month, name, amount));
        Planned = [.. Planned, planned];
        return planned;
    }

    public void UpdatePlanned(PlannedSpending planned) => Planned = Planned.Replacing(Valid(planned));

    public void RemovePlanned(Guid id) => Planned = Planned.Without(id);

    /// <summary>What changed since <paramref name="before"/>, for the next sync.</summary>
    public BudgetChanges ChangesSince(Budget before) => new(
        NetSalary == before.NetSalary ? null : NetSalary,
        ItemChanges.Between(before.Deductions, Deductions),
        ItemChanges.Between(before.Planned, Planned));

    /// <summary>The salary and items that <paramref name="touched"/> is about, as they are now.</summary>
    internal BudgetChanges Now(BudgetChanges touched) =>
        new(touched.NetSalary is null ? null : NetSalary, touched.Deductions.Now(Deductions), touched.Planned.Now(Planned));

    /// <summary>Takes over changes made on another device. All or nothing: throws if any of them breaks the rules.</summary>
    public void Apply(BudgetChanges changes)
    {
        var deductions = changes.Deductions.ApplyTo(Deductions, Valid);
        var planned = changes.Planned.ApplyTo(Planned, Valid);
        if (changes.NetSalary is { } salary) SetSalary(salary);
        Deductions = Sorted([.. deductions.Kept, .. deductions.Added]);
        Planned = [.. planned.Kept, .. planned.Added];
    }

    public MonthOverview Overview(int year, int month, DateOnly today)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new MonthOverview(year, month, NetSalary,
            [.. Deductions.Select(d =>
            {
                var date = new DateOnly(year, month, Math.Min(d.Day, lastDay));
                return new DeductionLine(d, date, date <= today);
            })],
            [.. Planned.Where(p => p.Year == year && p.Month == month)]);
    }

    private static Deduction Valid(Deduction d)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(d.Amount);
        ArgumentOutOfRangeException.ThrowIfLessThan(d.Day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(d.Day, 31);
        return d with { Name = d.Name.Required(nameof(d.Name)) };
    }

    private static PlannedSpending Valid(PlannedSpending p)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(p.Amount);
        _ = new DateOnly(p.Year, p.Month, 1); // throws for a month that doesn't exist
        return p with { Name = p.Name.Required(nameof(p.Name)) };
    }

    private static IReadOnlyList<Deduction> Sorted(IEnumerable<Deduction> deductions) => [.. deductions.OrderBy(d => d.Day)];
}
