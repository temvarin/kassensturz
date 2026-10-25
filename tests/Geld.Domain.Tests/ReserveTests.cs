namespace Geld.Domain.Tests;

/// <summary>The Rücklage: money besides the salary that covers months costing more than the salary.</summary>
public class ReserveTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    /// <summary>1.500 € net and 1.121,90 € fixed costs: 378,10 € free in a month with nothing planned.</summary>
    private static Budget Example(decimal? reserve = 2300)
    {
        var budget = new Budget();
        budget.SetSalary(1500);
        budget.AddDeduction("Miete", 950, 1);
        budget.AddDeduction("Rest", 171.90m, 15);
        if (reserve is { } amount) budget.SetReserve(amount, Today);
        return budget;
    }

    [Fact]
    public void A_month_that_costs_more_than_the_salary_is_covered_from_the_reserve()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Winterjacke", 578.10m);

        var october = budget.Overview(2026, 10, Today);

        Assert.Equal(-200m, october.Available);
        Assert.Equal((2300m, 200m, 0m, 2100m), (october.ReserveAtStart, october.FromReserve, october.Uncovered, october.ReserveAfter));
    }

    [Fact]
    public void A_month_within_the_salary_leaves_the_reserve_untouched()
    {
        var october = Example().Overview(2026, 10, Today);

        Assert.Equal((0m, 0m, 2300m), (october.FromReserve, october.Uncovered, october.ReserveAfter));
    }

    [Fact]
    public void Later_months_start_with_the_reserve_minus_earlier_shortfalls_but_surpluses_are_not_added()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Winterjacke", 578.10m); // 200 € short
        // November: nothing planned, 378,10 € left over; it may well be spent on everyday things, so it doesn't count.

        Assert.Equal(2100m, budget.Overview(2026, 11, Today).ReserveAtStart);
        Assert.Equal(2100m, budget.Overview(2026, 12, Today).ReserveAtStart);
    }

    [Fact]
    public void Only_what_the_reserve_cannot_cover_is_uncovered_and_the_reserve_never_goes_below_zero()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Winterjacke", 578.10m);
        budget.Plan(2026, 12, "Geschenke", 900);
        budget.Plan(2026, 12, "Anzahlung Urlaub", 1600);

        var december = budget.Overview(2026, 12, Today);

        Assert.Equal((2100m, 2100m, 21.90m, 0m), (december.ReserveAtStart, december.FromReserve, december.Uncovered, december.ReserveAfter));
        Assert.Equal(0m, budget.Overview(2027, 1, Today).ReserveAtStart);
    }

    [Fact]
    public void Without_a_reserve_the_whole_shortfall_is_uncovered()
    {
        var budget = Example(reserve: null);
        budget.Plan(2026, 10, "Winterjacke", 578.10m);

        var october = budget.Overview(2026, 10, Today);

        Assert.Null(october.ReserveAtStart);
        Assert.Null(october.ReserveAfter);
        Assert.Equal((0m, 200m), (october.FromReserve, october.Uncovered));
    }

    [Fact]
    public void Entering_the_reserve_again_starts_counting_from_that_month()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Winterjacke", 578.10m);

        budget.SetReserve(2500, new DateOnly(2026, 11, 3)); // after a look at the bank account

        Assert.Equal(new Reserve(2500, new DateOnly(2026, 11, 3)), budget.Reserve);
        Assert.Equal(2500m, budget.Overview(2026, 11, new DateOnly(2026, 11, 3)).ReserveAtStart);
    }

    [Fact]
    public void A_reserve_can_be_removed()
    {
        var budget = Example();

        budget.RemoveReserve();

        Assert.Null(budget.Reserve);
    }

    [Fact]
    public void A_reserve_is_never_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Budget().SetReserve(-1, Today));
}
