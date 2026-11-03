using Geld.Domain.SharedKernel;

namespace Geld.Domain.Budgeting;

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

/// <summary>The budget for reading: everything it shows and computes, nothing that changes it.</summary>
public interface IReadOnlyBudget
{
    decimal NetSalary { get; }
    IReadOnlyList<Deduction> Deductions { get; }
    IReadOnlyList<PlannedSpending> Planned { get; }
    Reserve? Reserve { get; }
    MonthOverview Overview(int year, int month, DateOnly today);
}

/// <summary>
/// The monthly budget: one net salary, fixed costs that recur every month, and spendings planned for single months.
/// There is no history: a changed salary or rent applies to every month, which is why only the current and
/// later months are shown. Persisted as part of <see cref="AppData"/>, in the shape of Contracts.BudgetJson.
/// </summary>
/// <remarks>State changes only through its methods, which keep its rules.</remarks>
public sealed class Budget : IReadOnlyBudget
{
    public Budget()
    {
    }

    /// <summary>A budget as it was saved, restored by the same rules as entering it: a budget that exists keeps them.</summary>
    /// <exception cref="ArgumentException">Something breaks the rules, e.g. a day 32 or an id twice.</exception>
    /// <param name="planned">In the order they were added, as <see cref="Planned"/>.</param>
    public Budget(decimal netSalary, IEnumerable<Deduction> deductions, IEnumerable<PlannedSpending> planned, Reserve? reserve)
    {
        SetSalary(netSalary);
        Deductions = Sorted(deductions.Valid(Valid));
        Planned = planned.Valid(Valid);
        Reserve = reserve is null ? null : Valid(reserve);
    }

    public decimal NetSalary { get; private set; }

    /// <summary>In the order they are debited.</summary>
    public IReadOnlyList<Deduction> Deductions { get; private set; } = [];

    /// <summary>In the order they were added.</summary>
    public IReadOnlyList<PlannedSpending> Planned { get; private set; } = [];

    /// <summary>
    /// The Rücklage: money besides the salary, e.g. on a savings account; null if not entered. A month that costs more
    /// than the salary is covered from it, and later months start with what is left. Surpluses are not added, because
    /// the app can't know whether they were saved or spent on everyday things; the user enters the amount anew now
    /// and then instead.
    /// </summary>
    public Reserve? Reserve { get; private set; }

    public void SetSalary(decimal netSalary)
    {
        if (!BudgetRules.IsSalary(netSalary)) throw new ArgumentOutOfRangeException(nameof(netSalary), netSalary, "Must not be negative.");
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

    public MonthOverview Overview(int year, int month, DateOnly today) =>
        WithoutReserve(year, month, today) with { ReserveAtStart = ReserveAt(new DateOnly(year, month, 1), today) };

    private MonthOverview WithoutReserve(int year, int month, DateOnly today)
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

    // What is left of the Rücklage when a month starts: the months since it was entered draw their shortfalls from it,
    // each computed as the month's own overview computes it.
    private decimal? ReserveAt(DateOnly month, DateOnly today)
    {
        if (Reserve is not { } reserve) return null;
        var left = reserve.Amount;
        for (var m = new DateOnly(reserve.AsOf.Year, reserve.AsOf.Month, 1); m < month; m = m.AddMonths(1))
            left = Math.Max(0, left - WithoutReserve(m.Year, m.Month, today).Shortfall);
        return left;
    }

    /// <summary>An independent copy, e.g. the base of a sync. Items are values and lists are replaced, never changed.</summary>
    public Budget Copy() => new() { NetSalary = NetSalary, Deductions = Deductions, Planned = Planned, Reserve = Reserve };

    private static Deduction Valid(Deduction d)
    {
        if (!BudgetRules.IsAmount(d.Amount)) throw new ArgumentOutOfRangeException(nameof(d), d.Amount, "The amount must be positive.");
        ArgumentOutOfRangeException.ThrowIfLessThan(d.Day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(d.Day, 31);
        return d with { Name = d.Name.Required(nameof(d.Name)) };
    }

    private static PlannedSpending Valid(PlannedSpending p)
    {
        if (!BudgetRules.IsAmount(p.Amount)) throw new ArgumentOutOfRangeException(nameof(p), p.Amount, "The amount must be positive.");
        _ = new DateOnly(p.Year, p.Month, 1); // throws for a month that doesn't exist
        return p with { Name = p.Name.Required(nameof(p.Name)) };
    }

    private static Reserve Valid(Reserve reserve)
    {
        if (!BudgetRules.IsReserve(reserve.Amount)) throw new ArgumentOutOfRangeException(nameof(reserve), reserve.Amount, "Must not be negative.");
        return reserve;
    }

    private static IReadOnlyList<Deduction> Sorted(IEnumerable<Deduction> deductions) => [.. deductions.OrderBy(d => d.Day)];
}
