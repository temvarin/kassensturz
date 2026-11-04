using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

public class MonthBudgetTests : AppTestContext
{
    public MonthBudgetTests()
    {
        Data.Budget.SetSalary(2850);
        Data.Budget.AddDeduction("Miete", 1150, new MonthlyOn(1));
        Data.Budget.AddDeduction("Handy", 20, new MonthlyOn(15));
        Data.Budget.Plan(2026, 10, "Make-up", 45);
        Data.Budget.Plan(2026, 11, "Geschenk Oma", 30);
    }

    [Fact]
    public void An_app_left_open_shows_the_new_month_when_it_comes_back_to_the_front()
    {
        var page = Render<MonthBudget>();
        Assert.Contains("Oktober 2026", page.Find(".month-nav").TextContent);

        Clock.Advance(TimeSpan.FromDays(18)); // 1 November
        Services.GetRequiredService<AppState>().Refresh();

        page.WaitForAssertion(() => Assert.Contains("November 2026", page.Find(".month-nav").TextContent));
    }

    [Fact]
    public void Editing_the_salary_starts_with_the_current_one()
    {
        var page = Render<MonthBudget>();

        Button(page, "Nettogehalt ändern").Click();

        Assert.Equal("2850,00", page.Find(".sheet input[inputmode=decimal]").GetAttribute("value"));
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
        Assert.Equal(new MonthlyOn(3), Stored.Budget.Deductions[1].Schedule);
    }

    [Fact]
    public void A_fixed_cost_can_repeat_every_few_weeks_and_shows_each_debit_in_its_month()
    {
        var page = Render<MonthBudget>();

        Button(page, "Fixkosten hinzufügen").Click();
        page.Find(".sheet input:not([inputmode])").Change("Kita-Essen");
        page.Find(".sheet input[inputmode=decimal]").Change("62");
        Button(page, "Alle paar Wochen").Click();
        page.Find(".sheet input[type=date]").Change("2026-10-05");
        page.Find(".sheet form").Submit(); // every 4 weeks unless changed

        Assert.Equal(new EveryWeeks(4, new DateOnly(2026, 10, 5)), Stored.Budget.Deductions.Single(d => d.Name == "Kita-Essen").Schedule);
        Button(page, "Nächster Monat").Click();
        var november = page.FindAll(".line").Where(line => line.TextContent.Contains("Kita-Essen", StringComparison.Ordinal)).ToList();
        Assert.Equal(["2.", "30."], november.Select(line => line.QuerySelector(".day")!.TextContent));
        Assert.All(november, line => Assert.Contains("alle 4 Wochen", line.TextContent));
    }

    [Fact]
    public void Editing_a_cost_every_few_weeks_starts_with_its_rhythm()
    {
        Data.Budget.AddDeduction("Kita-Essen", 62, new EveryWeeks(2, new DateOnly(2026, 10, 5)));
        var page = Render<MonthBudget>();

        page.FindAll(".line").First(line => line.TextContent.Contains("Kita-Essen", StringComparison.Ordinal)).Click();

        Assert.Equal("true", Button(page, "Alle paar Wochen").GetAttribute("aria-pressed"));
        Assert.Equal("2", page.Find(".sheet select").GetAttribute("value"));
        Assert.Equal("2026-10-05", page.Find(".sheet input[type=date]").GetAttribute("value"));
    }

    [Fact]
    public void A_fixed_cost_without_a_name_or_of_zero_is_refused_as_the_budget_would_refuse_it()
    {
        var page = Render<MonthBudget>();

        Button(page, "Fixkosten hinzufügen").Click();
        page.Find(".sheet input:not([inputmode])").Change("  ");
        page.Find(".sheet input[inputmode=decimal]").Change("0");
        page.Find(".sheet form").Submit();

        Assert.Equal(["Bitte benennen", "Z. B. 12,50 eingeben"], page.FindAll(".sheet .error").Select(e => e.TextContent));
        Assert.Empty(Stored.Budget.Deductions);
    }
}
