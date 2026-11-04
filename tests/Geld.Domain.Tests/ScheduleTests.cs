namespace Geld.Domain.Tests;

/// <summary>When fixed costs are debited: every month on a day, or every few weeks from a first date.</summary>
public class ScheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly EveryWeeks MealPlan = new(4, new DateOnly(2026, 10, 5));

    [Fact]
    public void Every_4_weeks_is_13_debits_a_year_so_some_months_have_two()
    {
        Assert.Empty(MealPlan.DatesIn(2026, 9)); // before the first debit
        Assert.Equal([new DateOnly(2026, 10, 5)], MealPlan.DatesIn(2026, 10));
        Assert.Equal([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30)], MealPlan.DatesIn(2026, 11));
        Assert.Equal(13, Enumerable.Range(1, 12).Sum(month => MealPlan.DatesIn(2027, month).Count()));
    }

    [Fact]
    public void A_monthly_cost_on_the_31st_falls_on_the_last_day_of_a_shorter_month() =>
        Assert.Equal([new DateOnly(2027, 2, 28)], new MonthlyOn(31).DatesIn(2027, 2));

    [Fact]
    public void A_month_with_two_debits_counts_both_and_lists_every_debit_by_date()
    {
        var budget = new Budget();
        budget.SetSalary(2850);
        budget.AddDeduction("Miete", 1150, new MonthlyOn(1));
        budget.AddDeduction("Kita-Essen", 62, MealPlan);
        budget.AddDeduction("Handy", 20, new MonthlyOn(15));

        var november = budget.Overview(2026, 11, Today);

        Assert.Equal(["Miete 1.11.", "Kita-Essen 2.11.", "Handy 15.11.", "Kita-Essen 30.11."],
            november.Deductions.Select(line => $"{line.Deduction.Name} {line.Date.Day}.{line.Date.Month}."));
        Assert.Equal(1150 + 62 + 20 + 62, november.FixedTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public void A_rhythm_of_no_weeks_or_more_than_a_year_is_refused(int weeks) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Budget().AddDeduction("Kita-Essen", 62, new EveryWeeks(weeks, Today)));

    [Fact]
    public void A_stored_cost_every_few_weeks_without_its_first_date_is_refused() =>
        Assert.Throws<ArgumentException>(() => new DeductionJson(Guid.NewGuid(), "Kita-Essen", 62, 5, EveryWeeks: 4).ToDomain());
}
