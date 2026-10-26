using System.Net;
using System.Net.Http.Json;
using Geld.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace Geld.Server.Tests;

/// <summary>The API behind Easy Auth, which passes the signed-in user on in request headers.</summary>
public class SignInTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Member = "8a1c0e52-member";
    private const string Stranger = "f39b77d0-stranger";

    private HttpClient Server(bool easyAuth = true, string members = $"{Member}, other-member") =>
        factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("Geld:Members", members);
            if (easyAuth) host.UseSetting("WEBSITE_AUTH_ENABLED", "True");
        }).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<HttpResponseMessage> Get(HttpClient server, string path, string? user, string? cookies = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (user is not null)
        {
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", user);
            request.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", $"{user}@example.com");
        }
        if (cookies is not null) request.Headers.Add("Cookie", cookies);
        return await server.SendAsync(request);
    }

    [Theory]
    [InlineData("/api/whoami")]
    [InlineData("/api/nothing")]
    public async Task The_api_asks_for_sign_in_rather_than_returning_the_app(string path)
    {
        var response = await Get(Server(), path, user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Member, true)]
    [InlineData(Stranger, false)]
    public async Task A_signed_in_user_learns_their_id_and_whether_they_are_a_member(string user, bool member)
    {
        var response = await Get(Server(), "/api/whoami", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new WhoAmI(user, $"{user}@example.com", member), await response.Content.ReadFromJsonAsync(ApiJson.Default.WhoAmI));
    }

    [Fact]
    public async Task The_session_cookie_comes_back_with_an_expiry_date_so_that_closing_the_app_keeps_the_sign_in()
    {
        const string session = "q8F+2b/x0=="; // Easy Auth's, which must come back exactly as it is

        var response = await Get(Server(), "/api/whoami", Member, $"Other=1; AppServiceAuthSession={session}");

        var cookie = Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList()));
        Assert.Equal(("AppServiceAuthSession", session, "/"), (cookie.Name.Value, cookie.Value.Value, cookie.Path.Value));
        Assert.InRange(cookie.Expires!.Value, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
        Assert.True(cookie.HttpOnly && cookie.Secure);
    }

    [Theory]
    [InlineData(null, "AppServiceAuthSession=q8F2bx0")] // Easy Auth refused the session: it stays refused
    [InlineData(Member, "Other=1")]
    public async Task Only_a_session_Easy_Auth_accepted_is_kept(string? user, string cookies)
    {
        var response = await Get(Server(), "/api/whoami", user, cookies);

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(Member, HttpStatusCode.NotFound)]
    [InlineData(Stranger, HttpStatusCode.Forbidden)]
    public async Task Only_members_get_past_sign_in(string user, HttpStatusCode expected)
    {
        var response = await Get(Server(), "/api/nothing", user);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Without_members_configured_nobody_is_a_member()
    {
        var response = await Get(Server(members: ""), "/api/nothing", Member);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.Unauthorized)]
    public async Task The_developer_s_user_works_only_on_the_developer_s_machine(string environment, HttpStatusCode expected)
    {
        var server = factory.WithWebHostBuilder(host => host
            .UseEnvironment(environment)
            .UseSetting("Geld:DevUser", Member)
            .UseSetting("Geld:Members", Member)).CreateClient();

        var response = await Get(server, "/api/whoami", user: null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Without_Easy_Auth_in_front_the_user_headers_are_ignored()
    {
        var response = await Get(Server(easyAuth: false), "/api/whoami", Member);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
