namespace Geld.Domain.Budgeting;

/// <summary>When a fixed cost is debited.</summary>
public abstract record Schedule
{
    /// <summary>The dates it is debited in one month, in order.</summary>
    public abstract IEnumerable<DateOnly> DatesIn(int year, int month);

    /// <summary>The day the list of fixed costs is sorted by.</summary>
    internal abstract int SortDay { get; }

    /// <exception cref="ArgumentOutOfRangeException">The schedule is impossible, e.g. a day 32.</exception>
    internal abstract Schedule Valid();
}

/// <summary>Every month on the same day; in shorter months on the last day.</summary>
/// <param name="Day">1–31.</param>
public sealed record MonthlyOn(int Day) : Schedule
{
    public override IEnumerable<DateOnly> DatesIn(int year, int month) =>
        [new DateOnly(year, month, Math.Min(Day, DateTime.DaysInMonth(year, month)))];

    internal override int SortDay => Day;

    internal override Schedule Valid()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Day, 31);
        return this;
    }
}

/// <summary>
/// Every few weeks from a first date. Every 4 weeks is 13 times a year, not 12, so some months have two debits.
/// </summary>
public sealed record EveryWeeks(int Weeks, DateOnly First) : Schedule
{
    public override IEnumerable<DateOnly> DatesIn(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var step = 7 * Weeks;
        var date = First;
        if (date < start)
        {
            var stepsToStart = (start.DayNumber - First.DayNumber + step - 1) / step; // rounded up
            date = First.AddDays(stepsToStart * step);
        }
        for (; date <= end; date = date.AddDays(step)) yield return date;
    }

    internal override int SortDay => First.Day;

    internal override Schedule Valid()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Weeks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Weeks, 52);
        return this;
    }
}
