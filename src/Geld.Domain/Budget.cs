using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>A fixed cost taken from the salary every month, e.g. rent or a subscription.</summary>
/// <param name="Day">Day of the month it is debited, 1–31; in shorter months the last day.</param>
public sealed record Deduction(Guid Id, string Name, decimal Amount, int Day) : IItem;

/// <summary>A spending the user plans for one particular month, e.g. make-up in October.</summary>
public sealed record PlannedSpending(Guid Id, int Year, int Month, string Name, decimal Amount) : IItem;

/// <summary>The Rücklage as the user entered it; see <see cref="Budget.Reserve"/>.</summary>
/// <param name="AsOf">The day it was entered. It counts from that day's month on.</param>
public sealed record Reserve(decimal Amount, DateOnly AsOf);

/// <param name="Date">When it is debited in this month.</param>
/// <param name="Done">Debited already: the date is today or earlier.</param>
public sealed record DeductionLine(Deduction Deduction, DateOnly Date, bool Done);

/// <summary>One month of the budget: the salary, what is taken from it, and what is left.</summary>
/// <param name="ReserveAtStart">What is left of the Rücklage when the month starts; null if none was entered.</param>
public sealed record MonthOverview(
    int Year, int Month, decimal Salary, IReadOnlyList<DeductionLine> Deductions, IReadOnlyList<PlannedSpending> Planned,
    decimal? ReserveAtStart = null)
{
    public decimal FixedTotal => Deductions.Sum(d => d.Deduction.Amount);
    public decimal PlannedTotal => Planned.Sum(p => p.Amount);

    /// <summary>Freely available after fixed costs and planned spendings; negative when overplanned.</summary>
    public decimal Available => Salary - FixedTotal - PlannedTotal;

    /// <summary>Fixed costs not yet debited this month.</summary>
    public decimal StillToPay => Deductions.Where(d => !d.Done).Sum(d => d.Deduction.Amount);

    /// <summary>What the month costs beyond the salary.</summary>
    public decimal Shortfall => Math.Max(0, -Available);

    /// <summary>The part of the shortfall the Rücklage covers.</summary>
    public decimal FromReserve => Math.Min(Shortfall, ReserveAtStart ?? 0);

    /// <summary>The part of the shortfall nothing covers.</summary>
    public decimal Uncovered => Shortfall - FromReserve;

    public decimal? ReserveAfter => ReserveAtStart - FromReserve;
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

    /// <summary>
    /// The Rücklage: money besides the salary, e.g. on a savings account; null if not entered. A month that costs more
    /// than the salary is covered from it, and later months start with what is left. Surpluses are not added, because
    /// the app can't know whether they were saved or spent on everyday things; the user enters the amount anew now
    /// and then instead.
    /// </summary>
    [JsonInclude] public Reserve? Reserve { get; internal set; }

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

    /// <param name="today">From this month on, the amount counts as it is.</param>
    public void SetReserve(decimal amount, DateOnly today) => Reserve = Valid(new Reserve(amount, today));

    public void RemoveReserve() => Reserve = null;

    /// <summary>What changed since <paramref name="before"/>, for the next sync.</summary>
    public BudgetChanges ChangesSince(Budget before) => new(
        NetSalary == before.NetSalary ? null : NetSalary,
        ItemChanges.Between(before.Deductions, Deductions),
        ItemChanges.Between(before.Planned, Planned),
        Reserve == before.Reserve ? null : new ReserveChange(Reserve));

    /// <summary>The salary and items that <paramref name="touched"/> is about, as they are now.</summary>
    internal BudgetChanges Now(BudgetChanges touched) =>
        new(touched.NetSalary is null ? null : NetSalary, touched.Deductions.Now(Deductions), touched.Planned.Now(Planned),
            touched.Reserve is null ? null : new ReserveChange(Reserve));

    /// <summary>Takes over changes made on another device. All or nothing: throws if any of them breaks the rules.</summary>
    public void Apply(BudgetChanges changes)
    {
        var deductions = changes.Deductions.ApplyTo(Deductions, Valid);
        var planned = changes.Planned.ApplyTo(Planned, Valid);
        var reserve = changes.Reserve is { Value: { } r } ? Valid(r) : null;
        if (changes.NetSalary is { } salary) SetSalary(salary);
        if (changes.Reserve is not null) Reserve = reserve;
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
            [.. Planned.Where(p => p.Year == year && p.Month == month)],
            ReserveAt(new DateOnly(year, month, 1)));
    }

    // What is left of the Rücklage when a month starts: the months since it was entered draw their shortfalls from it.
    private decimal? ReserveAt(DateOnly month)
    {
        if (Reserve is not { } reserve) return null;
        var left = reserve.Amount;
        for (var m = new DateOnly(reserve.AsOf.Year, reserve.AsOf.Month, 1); m < month; m = m.AddMonths(1))
        {
            var available = NetSalary - Deductions.Sum(d => d.Amount) - Planned.Where(p => p.Year == m.Year && p.Month == m.Month).Sum(p => p.Amount);
            left = Math.Max(0, left + Math.Min(0, available));
        }
        return left;
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

    private static Reserve Valid(Reserve reserve)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reserve.Amount);
        return reserve;
    }

    private static IReadOnlyList<Deduction> Sorted(IEnumerable<Deduction> deductions) => [.. deductions.OrderBy(d => d.Day)];
}
