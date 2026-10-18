using System.Net;
using Geld.App.Components;
using Geld.App.Pages;
using Geld.App.Services;
using Geld.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

public class SyncTests : AppTestContext
{
    public SyncTests() => Server.User = FakeServer.Anna;

    private AppState State => Services.GetRequiredService<AppState>();
    private SyncService Sync => Services.GetRequiredService<SyncService>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    /// <summary>The account card, once the sync at start has finished.</summary>
    private IRenderedComponent<AccountCard> Started(string status = "gerade eben")
    {
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Equal(status, card.Find(".sync").TextContent));
        return card;
    }

    private static HttpResponseMessage Error(HttpStatusCode status) => new(status);

    [Fact]
    public void A_device_that_never_synced_adds_its_data_at_start_and_gets_the_shared_pot()
    {
        var mine = Data.Kindergeld.Receive(Today, 518);
        var theirs = Server.Kindergeld.Spend(Today, 49.99m, "Kleidung");

        Started();

        Assert.Equal([mine, theirs], Server.Kindergeld.Entries); // on the same day, the one the server got last leads
        Assert.Equal([mine, theirs], Stored.Kindergeld.Entries);
        Assert.Equal(FakeServer.Anna.Id, Stored.Sync?.UserId);
    }

    [Fact]
    public void A_save_is_sent_two_seconds_later()
    {
        var card = Started();
        Data.Kindergeld.Receive(Today, 518);
        State.Save();
        Assert.Single(Server.Syncs);

        Clock.Advance(SyncService.AfterSave);

        card.WaitForAssertion(() => Assert.Single(Server.Kindergeld.Entries));
        Assert.Equal(2, Server.Syncs.Count);
    }

    [Fact]
    public async Task Coming_back_to_the_app_fetches_the_other_phone_s_changes_unless_it_just_did()
    {
        Started();
        var theirs = Server.Kindergeld.Receive(Today, 518); // meanwhile, on the other phone

        Clock.Advance(SyncService.Recent - TimeSpan.FromSeconds(1));
        await Sync.ResumeAsync();
        Assert.Single(Server.Syncs);

        Clock.Advance(TimeSpan.FromSeconds(1));
        await Sync.ResumeAsync();
        Assert.Equal([theirs], Data.Kindergeld.Entries);
    }

    [Fact]
    public async Task An_app_opened_offline_syncs_when_it_comes_back_online()
    {
        Server.Override = _ => throw new HttpRequestException("offline");
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Equal("Server nicht erreichbar", card.Find(".account").TextContent));

        Server.Override = null;
        await Sync.ResumeAsync();

        card.WaitForAssertion(() => Assert.Equal("gerade eben", card.Find(".sync").TextContent));
    }

    [Fact]
    public void A_change_made_during_a_sync_waits_for_it_and_then_goes_out()
    {
        Server.Hold = new TaskCompletionSource();
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Equal("läuft …", card.Find(".sync").TextContent));

        var payment = Data.Kindergeld.Receive(Today, 518);
        State.Save();
        Clock.Advance(SyncService.AfterSave);
        Assert.Single(Server.Syncs); // one sync at a time
        Server.Hold.SetResult();

        card.WaitForAssertion(() => Assert.Equal(2, Server.Syncs.Count));
        card.WaitForAssertion(() => Assert.Equal("gerade eben", card.Find(".sync").TextContent));
        Assert.Equal([payment], Server.Kindergeld.Entries);
        Assert.Equal([payment], Stored.Kindergeld.Entries);
    }

    [Fact]
    public void A_failed_sync_is_tried_once_more_after_30_seconds()
    {
        Server.Next.Enqueue(() => Error(HttpStatusCode.ServiceUnavailable));
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Single(Server.Syncs));

        Clock.Advance(SyncService.RetryAfter);

        card.WaitForAssertion(() => Assert.Equal("gerade eben", card.Find(".sync").TextContent));
    }

    [Fact]
    public void When_the_second_try_fails_too_the_changes_wait_for_the_next_sync()
    {
        Server.Next.Enqueue(() => Error(HttpStatusCode.InternalServerError));
        Server.Next.Enqueue(() => throw new HttpRequestException("offline"));
        Data.Kindergeld.Receive(Today, 518);
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Single(Server.Syncs));
        Clock.Advance(SyncService.RetryAfter);

        card.WaitForAssertion(() => Assert.Equal("fehlgeschlagen", card.Find(".sync").TextContent));
        Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, Server.Syncs.Count); // no endless retrying
        Assert.True(Data.HasUnsyncedChanges);

        Button(card, "Jetzt synchronisieren").Click();

        card.WaitForAssertion(() => Assert.Single(Server.Kindergeld.Entries));
    }

    [Fact]
    public void An_entry_deleted_while_a_lost_sync_is_retried_stays_deleted()
    {
        var shoes = Data.Kindergeld.Spend(Today, 35, "Kleidung");
        Server.LoseNextAnswer = true;
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Single(Server.Kindergeld.Entries)); // saved, but the app doesn't know

        Data.Kindergeld.Remove(shoes.Id);
        State.Save();
        Clock.Advance(SyncService.RetryAfter);

        card.WaitForAssertion(() => Assert.Equal("gerade eben", card.Find(".sync").TextContent));
        Assert.Empty(Server.Kindergeld.Entries);
        Assert.Empty(Stored.Kindergeld.Entries);
    }

    [Fact]
    public void When_another_account_signed_in_meanwhile_nothing_is_sent_for_it()
    {
        Started();
        Server.User = FakeServer.Ben; // signed in in another tab; this app still thinks Anna is
        Server.Next.Enqueue(() => Error(HttpStatusCode.PreconditionFailed));
        var card = Render<AccountCard>();

        Data.Budget.SetSalary(2850);
        State.Save();
        Clock.Advance(SyncService.AfterSave);

        card.WaitForAssertion(() => Button(card, "Daten auf diesem Gerät zurücksetzen"));
        Assert.DoesNotContain(FakeServer.Ben.Id, Server.Budgets.Keys);
    }

    [Fact]
    public void An_app_older_than_the_server_asks_to_be_restarted()
    {
        Server.Next.Enqueue(() => Error(HttpStatusCode.Conflict));

        var card = Started("Bitte App neu laden");

        Assert.Contains("neue Version", card.Markup);
        Assert.Empty(card.FindAll("button.primary"));
    }

    [Fact]
    public void When_the_sign_in_has_expired_it_asks_to_sign_in_again()
    {
        Server.Next.Enqueue(() => Error(HttpStatusCode.Unauthorized));

        var card = Render<AccountCard>();

        card.WaitForAssertion(() => Button(card, "Mit Microsoft anmelden"));
        Assert.Equal("nein", card.Find(".account").TextContent);
    }

    [Fact]
    public void Data_of_another_account_is_not_synced_until_the_device_is_reset()
    {
        Data.Kindergeld.Receive(Today, 100);
        Data.CompleteSync(Data.StartSync(FakeServer.Ben.Id), new SyncResponse(new KindergeldPot(), new Budget()), Clock.GetUtcNow());
        var annas = Server.Kindergeld.Receive(Today, 518);
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Button(card, "Daten auf diesem Gerät zurücksetzen"));
        Assert.Empty(Server.Syncs);
        Dialogs.Changed += () => Dialogs.Close(Dialogs.Current is { Cancel: not null });

        Button(card, "Daten auf diesem Gerät zurücksetzen").Click();

        card.WaitForAssertion(() => Assert.Equal("gerade eben", card.Find(".sync").TextContent));
        Assert.Equal([annas], Stored.Kindergeld.Entries);
        Assert.Equal(FakeServer.Anna.Id, Stored.Sync?.UserId);
        Assert.Equal([annas], Server.Kindergeld.Entries);
    }

    [Fact]
    public void A_phone_that_synced_before_and_is_signed_out_now_says_so_on_its_pages_and_signs_in_from_there()
    {
        Started();
        Server.User = null; // the session has expired
        Navigation.NavigateTo("budget");
        var page = Render<MonthBudget>();
        Assert.Empty(page.FindAll("button.banner"));

        Data.Kindergeld.Receive(Today, 518);
        State.Save();
        Clock.Advance(SyncService.AfterSave);
        page.WaitForAssertion(() => Assert.Contains("nicht synchronisiert", page.Find("button.banner").TextContent));
        page.Find("button.banner").Click();

        Assert.Equal("http://localhost/.auth/login/aad?post_login_redirect_uri=/budget",
            Navigation.Uri);
    }

    [Fact]
    public async Task Without_ever_having_synced_being_signed_out_is_nothing_to_warn_about()
    {
        Server.User = null;
        var page = Render<Home>();

        await Sync.StartAsync();

        Assert.Equal(SyncStatus.SignedOut, Sync.Status);
        Assert.Empty(page.FindAll("button.banner"));
    }

    [Fact]
    public async Task Open_pages_show_what_a_sync_brings()
    {
        Server.Kindergeld.Receive(Today, 518);
        var budget = Server.Budgets[FakeServer.Anna.Id] = new Budget();
        budget.AddDeduction("Miete", 1150, 1); // from Anna's other device
        var home = Render<Home>();
        var month = Render<MonthBudget>();

        await Sync.StartAsync();

        home.WaitForAssertion(() => Assert.Equal("518,00 €", home.Find(".big").TextContent));
        month.WaitForAssertion(() => Assert.Contains("Miete", month.Find(".list").TextContent));
    }

    [Fact]
    public void An_entry_deleted_by_a_sync_while_it_is_being_edited_is_saved_as_a_new_one()
    {
        var entry = Data.Kindergeld.Receive(Today, 518);
        var page = Render<Home>();
        page.Find(".line").Click();

        Data.Kindergeld.Remove(entry.Id); // what a sync does when the other phone deleted it
        page.Find(".sheet input[inputmode=decimal]").Change("259");
        page.Find(".sheet form").Submit();

        Assert.Equal(259m, Assert.Single(Stored.Kindergeld.Entries).Amount);
    }
}
