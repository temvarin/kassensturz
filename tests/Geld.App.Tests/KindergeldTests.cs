using Geld.App.Pages;
using Geld.Domain;

namespace Geld.App.Tests;

public class KindergeldTests : AppTestContext
{
    [Fact]
    public void One_tap_books_a_month_of_Kindergeld_for_all_children_and_keeps_it()
    {
        Data.Kindergeld.Configure(children: 2, endDigit: 4);
        var page = Render<Home>();

        Button(page, "518,00 € erhalten").Click();

        Assert.Equal("518,00 €", page.Find(".big").TextContent);
        var entry = Assert.Single(Stored.Kindergeld.Entries);
        Assert.Equal((EntryKind.Payment, 518m, Today), (entry.Kind, entry.Amount, entry.Date));
    }

    [Fact]
    public void Shows_when_the_next_Kindergeld_comes()
    {
        Data.Kindergeld.Configure(children: 1, endDigit: 4);

        var next = Render<Home>().Find(".next");

        Assert.Contains("11. November", next.TextContent);
        Assert.Contains("in 28 Tagen", next.TextContent);
    }

    [Fact]
    public void Without_the_endziffer_it_asks_for_it_in_the_settings()
    {
        var next = Render<Home>().Find(".next");

        Assert.Equal("einstellungen", next.GetAttribute("href"));
        Assert.Contains("Endziffer eintragen", next.TextContent);
    }

    [Fact]
    public void A_spending_is_entered_with_its_category_and_lowers_the_balance()
    {
        Data.Kindergeld.Receive(Today, 259);
        var page = Render<Home>();

        Button(page, "Ausgabe").Click();
        page.Find(".sheet input[inputmode=decimal]").Change("49,99");
        page.Find(".sheet input[list=categories]").Change("Kleidung");
        page.Find(".sheet form").Submit();

        Assert.Equal("209,01 €", page.Find(".big").TextContent);
        Assert.Empty(page.FindAll(".sheet"));
        Assert.Contains("Kleidung", page.Find(".line").TextContent);
    }

    [Fact]
    public void An_unreadable_amount_is_explained_and_nothing_is_booked()
    {
        var page = Render<Home>();

        Button(page, "Ausgabe").Click();
        page.Find(".sheet input[inputmode=decimal]").Change("zwölf");
        page.Find(".sheet form").Submit();

        Assert.Equal("Z. B. 12,50 eingeben", page.Find(".sheet .error").TextContent);
        Assert.Empty(Data.Kindergeld.Entries);
    }

    [Fact]
    public void Tapping_an_entry_opens_it_for_editing_and_it_can_be_deleted()
    {
        Data.Kindergeld.Receive(Today, 518);
        var page = Render<Home>();

        page.Find(".line").Click();
        Assert.Equal("518,00", page.Find(".sheet input[inputmode=decimal]").GetAttribute("value"));
        Button(page, "Löschen").Click();

        Assert.Empty(Stored.Kindergeld.Entries);
        Assert.Equal("0,00 €", page.Find(".big").TextContent);
    }
}
