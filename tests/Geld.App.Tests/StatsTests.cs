namespace Geld.App.Tests;

/// <summary>The statistics page: spending per category for a period, and money in and out per month.</summary>
public class StatsTests : AppTestContext
{
    private static string[] Categories(IRenderedComponent<Stats> page) =>
        [.. page.FindAll(".bar-row").Select(row => $"{row.Children[0].TextContent} {row.Children[1].TextContent}")];

    [Fact]
    public void Spending_per_category_is_ranked_for_the_chosen_period()
    {
        Data.Kindergeld.Spend(Today, 50, "Kleidung");
        Data.Kindergeld.Spend(Today, 20, "Spielzeug");
        Data.Kindergeld.Spend(new DateOnly(2026, 3, 2), 30, "Kleidung");
        Data.Kindergeld.Spend(new DateOnly(2025, 12, 20), 100, "Freizeit & Kurse");
        var page = Render<Stats>();

        Assert.Equal(["Kleidung 80,00 €", "Spielzeug 20,00 €"], Categories(page)); // this year, the default

        Button(page, "Monat").Click();
        Assert.Equal(["Kleidung 50,00 €", "Spielzeug 20,00 €"], Categories(page));

        Button(page, "Gesamt").Click();
        Assert.Equal(["Freizeit & Kurse 100,00 €", "Kleidung 80,00 €", "Spielzeug 20,00 €"], Categories(page));
    }

    [Fact]
    public void Months_are_listed_newest_first_from_the_first_one_with_an_entry()
    {
        Data.Kindergeld.Receive(new DateOnly(2026, 8, 12), 518);
        Data.Kindergeld.Spend(Today, 49.99m, "Kleidung");

        var page = Render<Stats>();

        Assert.Equal(["Okt", "Sep", "Aug"], page.FindAll(".months .label").Select(label => label.TextContent));
    }

    [Fact]
    public void Without_entries_the_page_says_so()
    {
        var page = Render<Stats>();

        Assert.Contains("Keine Ausgaben in diesem Jahr.", page.Markup);
        Assert.Contains("Noch keine Buchungen in den letzten zwölf Monaten.", page.Markup);
    }
}
