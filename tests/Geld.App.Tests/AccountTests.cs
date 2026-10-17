using System.Net;
using System.Net.Http.Json;
using Geld.App.Components;
using Geld.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

public class AccountTests : AppTestContext
{
    private string Address => Services.GetRequiredService<NavigationManager>().Uri;

    private static HttpResponseMessage SignedIn(bool member) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new WhoAmI("8a1c0e52", "anna@example.com", member), ApiJson.Default.WhoAmI) };

    [Fact]
    public void Signed_out_it_offers_the_Microsoft_sign_in_which_returns_to_the_settings()
    {
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Equal("nein", card.Find(".account").TextContent));

        Button(card, "Mit Microsoft anmelden").Click();

        Assert.Equal("http://localhost/.auth/login/aad?post_login_redirect_uri=/einstellungen", Address);
    }

    [Fact]
    public void A_member_sees_their_account_and_can_sign_out()
    {
        Server = _ => SignedIn(member: true);
        var card = Render<AccountCard>();
        card.WaitForAssertion(() => Assert.Equal("anna@example.com", card.Find(".account").TextContent));
        Assert.Empty(card.FindAll("code"));

        Button(card, "Abmelden").Click();

        Assert.Equal("http://localhost/.auth/logout?post_logout_redirect_uri=/einstellungen", Address);
    }

    [Fact]
    public void Someone_not_yet_on_the_members_list_sees_the_id_to_send_in()
    {
        Server = _ => SignedIn(member: false);
        var card = Render<AccountCard>();

        card.WaitForAssertion(() => Assert.Equal("8a1c0e52", card.Find("code").TextContent));
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("error")]
    [InlineData("no api")] // the development server answers every path with the app
    public void Without_the_server_it_says_so(string problem)
    {
        Server = _ => problem switch
        {
            "offline" => throw new HttpRequestException("offline"),
            "error" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!DOCTYPE html>", null, "text/html") },
        };
        var card = Render<AccountCard>();

        card.WaitForAssertion(() => Assert.Equal("Server nicht erreichbar", card.Find(".account").TextContent));
        Assert.Empty(card.FindAll("button"));
    }
}
