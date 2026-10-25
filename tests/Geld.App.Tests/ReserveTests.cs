using Geld.App.Pages;
using Geld.Domain;

namespace Geld.App.Tests;

/// <summary>The Rücklage on the budget page.</summary>
public class ReserveTests : AppTestContext
{
    public ReserveTests()
    {
        Data.Budget.SetSalary(2850);
        Data.Budget.AddDeduction("Miete", 1150, 1);
        Data.Budget.AddDeduction("Handy", 20, 15);
        Data.Budget.Plan(2026, 10, "Urlaub", 1880); // 200 € more than the salary leaves
    }

    [Fact]
    public void A_month_covered_from_the_reserve_says_so_instead_of_turning_red()
    {
        Data.Budget.SetReserve(2300, Today);

        var page = Render<MonthBudget>();

        Assert.Equal("−200,00 €", page.Find(".big").TextContent);
        Assert.DoesNotContain("negative", page.Find(".big").ClassName);
        Assert.Equal("Gedeckt aus der Rücklage", page.Find(".status").TextContent.Trim());
        var card = page.Find(".reserve-card").TextContent;
        Assert.Contains("Stand 14.10.", card);
        Assert.Contains("2.300,00 €", card);
        Assert.Contains("danach 2.100,00 €", card);
    }

    [Fact]
    public void A_later_month_shows_what_is_left_and_only_the_uncovered_part_is_red()
    {
        Data.Budget.SetReserve(2300, Today);
        Data.Budget.Plan(2026, 12, "Geschenke", 3801.90m);
        var page = Render<MonthBudget>();

        Button(page, "Nächster Monat").Click();
        Assert.Contains("Anfang November", page.Find(".reserve-card").TextContent);
        Assert.Contains("2.100,00 €", page.Find(".reserve-card").TextContent);
        Button(page, "Nächster Monat").Click();

        Assert.Equal("Nicht gedeckt: 21,90 €", page.Find(".status").TextContent.Trim());
        Assert.Contains("negative", page.Find(".big").ClassName);
        Assert.Contains("danach 0,00 €", page.Find(".reserve-card").TextContent);
    }

    [Fact]
    public void Without_a_reserve_a_card_invites_to_enter_one_which_counts_from_today()
    {
        var page = Render<MonthBudget>();
        Assert.Equal("Nicht gedeckt: 200,00 €", page.Find(".status").TextContent.Trim());

        page.Find(".reserve-card").Click();
        page.Find(".sheet input[inputmode=decimal]").Change("2300");
        page.Find(".sheet form").Submit();

        Assert.Equal(new Reserve(2300, Today), Stored.Budget.Reserve);
        Assert.Empty(page.FindAll(".sheet"));
        Assert.Equal("Gedeckt aus der Rücklage", page.Find(".status").TextContent.Trim());
    }

    [Fact]
    public void Editing_starts_with_the_amount_and_the_reserve_can_be_removed()
    {
        Data.Budget.SetReserve(2300, Today);
        var page = Render<MonthBudget>();

        page.Find(".reserve-card").Click();
        Assert.Equal("2300,00", page.Find(".sheet input[inputmode=decimal]").GetAttribute("value"));
        Button(page, "Rücklage entfernen").Click();

        Assert.Null(Stored.Budget.Reserve);
        Assert.Contains("Rücklage eintragen", page.Find(".reserve-card").TextContent);
    }
}
