using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

/// <summary>Stored data this version can't read is kept aside, not overwritten (DataStore.Load).</summary>
public class UnreadableTests : AppTestContext
{
    // E.g. written by a newer version, before a rollback.
    private const string Unreadable = """{"schema":2,"data":{"kindergeld":{},"budget":{}}}""";

    public UnreadableTests() => Services.GetRequiredService<IKeyValueStore>().Write("geld.data", Unreadable);

    [Fact]
    public void The_pages_say_why_the_app_starts_empty()
    {
        var home = Render<Home>();

        Assert.True(Data.IsEmpty);
        Assert.Contains("ließen sich nicht lesen", home.Find(".banner").TextContent);
        Assert.Contains("ließen sich nicht lesen", Render<MonthBudget>().Find(".banner").TextContent);
    }

    [Fact]
    public void Booking_after_the_fresh_start_keeps_the_unreadable_data()
    {
        var home = Render<Home>();

        Button(home, "259,00 € erhalten").Click();

        Assert.Equal(259m, Stored.Kindergeld.Received);
        Assert.Equal(Unreadable, Services.GetRequiredService<DataStore>().Unreadable);
    }

    [Fact]
    public void The_data_can_be_saved_as_a_file_exactly_as_it_was_stored()
    {
        JSInterop.Setup<bool>("geld.saveFile", _ => true).SetResult(true);

        Button(Render<Settings>(), "Als Datei speichern").Click();

        var saved = JSInterop.VerifyInvoke("geld.saveFile").Arguments;
        Assert.Equal(["kassensturz-nicht-lesbar-2026-10-14.json", Unreadable], saved);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Discarding_the_data_asks_first(bool discard, bool kept)
    {
        var settings = Render<Settings>();
        string? asked = null;
        Dialogs.Changed += () =>
        {
            if (Dialogs.Current is not { Cancel: not null } question) return;
            asked = question.Message;
            Dialogs.Close(discard);
        };

        Button(settings, "Verwerfen").Click();

        Assert.StartsWith("Die nicht lesbaren Daten endgültig löschen?", asked);
        Assert.Equal(kept, Services.GetRequiredService<DataStore>().Unreadable is not null);
        Assert.Equal(kept, settings.FindAll("button").Any(b => b.TextContent.Trim() == "Verwerfen"));
        Assert.Equal(kept, Render<Home>().FindAll(".banner").Count == 1);
    }
}
