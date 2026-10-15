using Geld.App.Pages;

namespace Geld.App.Tests;

public class MonthBudgetTests : AppTestContext
{
    public MonthBudgetTests()
    {
        Data.Budget.SetSalary(2850);
        Data.Budget.AddDeduction("Miete", 1150, 1);
        Data.Budget.AddDeduction("Handy", 20, 15);
        Data.Budget.Plan(2026, 10, "Make-up", 45);
        Data.Budget.Plan(2026, 11, "Geschenk Oma", 30);
    }

    [Fact]
    public void Shows_what_is_freely_available_after_fixed_costs_and_plans() =>
        Assert.Equal("1.635,00 €", Render<MonthBudget>().Find(".big").TextContent);

    [Fact]
    public void Debited_costs_are_struck_through_and_the_next_one_is_marked()
    {
        var lines = Render<MonthBudget>().FindAll(".line.done, .line:has(.badge)");

        Assert.Equal(2, lines.Count);
        Assert.Contains("Miete", lines[0].TextContent);
        Assert.Contains("morgen", lines[1].TextContent);
    }

    [Fact]
    public void The_next_month_shows_only_what_is_planned_for_it()
    {
        var page = Render<MonthBudget>();

        Button(page, "Nächster Monat").Click();

        Assert.Equal("November 2026", page.Find(".month-nav span").TextContent);
        Assert.Contains("Geschenk Oma", page.Markup);
        Assert.DoesNotContain("Make-up", page.Markup);
        Assert.Equal("1.650,00 €", page.Find(".big").TextContent);
    }

    [Fact]
    public void Past_months_are_not_shown() =>
        Assert.True(Button(Render<MonthBudget>(), "Vorheriger Monat").HasAttribute("disabled"));

    [Fact]
    public void A_spending_planned_in_a_later_month_belongs_to_that_month()
    {
        var page = Render<MonthBudget>();
        Button(page, "Nächster Monat").Click();

        Button(page, "Geplante Ausgabe hinzufügen").Click();
        page.Find(".sheet input:not([inputmode])").Change("Winterreifen");
        page.Find(".sheet input[inputmode=decimal]").Change("400");
        page.Find(".sheet form").Submit();

        Assert.Equal((2026, 11), Stored.Budget.Planned.Where(p => p.Name == "Winterreifen").Select(p => (p.Year, p.Month)).Single());
    }

    [Fact]
    public void A_fixed_cost_is_added_with_its_day()
    {
        var page = Render<MonthBudget>();

        Button(page, "Fixkosten hinzufügen").Click();
        page.Find(".sheet input:not([inputmode])").Change("Spotify");
        page.Find(".sheet input[inputmode=decimal]").Change("11,99");
        page.Find(".sheet select").Change("3");
        page.Find(".sheet form").Submit();

        Assert.Equal(["Miete", "Spotify", "Handy"], Stored.Budget.Deductions.Select(d => d.Name));
        Assert.Equal(3, Stored.Budget.Deductions[1].Day);
    }
}
