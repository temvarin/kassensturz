namespace Geld.Domain.Tests;

public class BudgetTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static Budget Example()
    {
        var budget = new Budget();
        budget.SetSalary(2850);
        budget.AddDeduction("Fitnessstudio", 29.90m, 28);
        budget.AddDeduction("Miete", 1150, 1);
        budget.AddDeduction("Handy", 20, 15);
        budget.AddDeduction("Spotify", 11.99m, 3);
        return budget;
    }

    [Fact]
    public void Available_is_the_salary_minus_fixed_costs_and_planned_spendings()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Make-up", 45);

        var october = budget.Overview(2026, 10, Today);

        Assert.Equal((1211.89m, 45m, 1593.11m), (october.FixedTotal, october.PlannedTotal, october.Available));
    }

    [Fact]
    public void Available_goes_negative_when_more_is_planned_than_there_is()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Urlaub", 2000);

        Assert.Equal(-361.89m, budget.Overview(2026, 10, Today).Available);
    }

    [Fact]
    public void Deductions_are_listed_in_the_order_they_are_debited() =>
        Assert.Equal(["Miete", "Spotify", "Handy", "Fitnessstudio"], Example().Deductions.Select(d => d.Name));

    [Fact]
    public void Deductions_up_to_today_are_done_the_rest_are_still_to_pay()
    {
        var october = Example().Overview(2026, 10, Today);

        Assert.Equal([true, true, false, false], october.Deductions.Select(d => d.Done));
        Assert.Equal(49.90m, october.StillToPay);
    }

    [Fact]
    public void A_deduction_due_today_counts_as_done() =>
        Assert.True(Example().Overview(2026, 10, new DateOnly(2026, 10, 15)).Deductions.Single(d => d.Deduction.Name == "Handy").Done);

    [Fact]
    public void In_a_later_month_nothing_is_debited_yet() =>
        Assert.All(Example().Overview(2026, 11, Today).Deductions, d => Assert.False(d.Done));

    [Fact]
    public void A_deduction_on_the_31st_falls_on_the_last_day_of_a_shorter_month()
    {
        var budget = new Budget();
        budget.AddDeduction("Versicherung", 46.30m, 31);

        Assert.Equal(new DateOnly(2027, 2, 28), budget.Overview(2027, 2, Today).Deductions.Single().Date);
        Assert.Equal(new DateOnly(2026, 11, 30), budget.Overview(2026, 11, Today).Deductions.Single().Date);
    }

    [Fact]
    public void Planned_spendings_count_only_in_their_month()
    {
        var budget = Example();
        budget.Plan(2026, 10, "Make-up", 45);
        budget.Plan(2026, 11, "Geschenk Oma", 30);

        Assert.Equal(["Make-up"], budget.Overview(2026, 10, Today).Planned.Select(p => p.Name));
        Assert.Equal(["Geschenk Oma"], budget.Overview(2026, 11, Today).Planned.Select(p => p.Name));
        Assert.Empty(budget.Overview(2026, 12, Today).Planned);
    }

    [Fact]
    public void A_changed_deduction_keeps_its_place_in_the_debit_order()
    {
        var budget = Example();
        var rent = budget.Deductions.Single(d => d.Name == "Miete");

        budget.UpdateDeduction(rent with { Amount = 1200, Day = 20 });

        Assert.Equal(["Spotify", "Handy", "Miete", "Fitnessstudio"], budget.Deductions.Select(d => d.Name));
        Assert.Equal(1200m, budget.Deductions.Single(d => d.Id == rent.Id).Amount);
    }

    [Fact]
    public void Items_can_be_removed()
    {
        var budget = Example();
        var plan = budget.Plan(2026, 10, "Make-up", 45);

        budget.RemovePlanned(plan.Id);
        budget.RemoveDeduction(budget.Deductions[0].Id);

        Assert.Empty(budget.Planned);
        Assert.Equal(3, budget.Deductions.Count);
    }

    [Theory]
    [InlineData("", 10, 1)]
    [InlineData("Miete", 0, 1)]
    [InlineData("Miete", 10, 0)]
    [InlineData("Miete", 10, 32)]
    public void Rejects_a_deduction_without_name_amount_or_valid_day(string name, int amount, int day) =>
        Assert.ThrowsAny<ArgumentException>(() => new Budget().AddDeduction(name, amount, day));
}
