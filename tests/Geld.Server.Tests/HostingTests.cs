using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Geld.Server.Tests;

public class HostingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData("/")]
    [InlineData("/budget")] // a route inside the app, e.g. after a reload
    [InlineData("/index.html")] // downloaded by the service worker
    public async Task App_routes_are_answered_with_the_start_page_which_is_never_cached(string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Kassensturz", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task An_unknown_api_route_is_not_found_rather_than_the_app()
    {
        var response = await client.GetAsync(new Uri("/api/nothing", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Every_script_the_start_page_loads_is_served()
    {
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        var scripts = Regex.Matches(html, """<script src="([^"]+)">""").Select(m => m.Groups[1].Value).ToList();

        Assert.Contains("_framework/blazor.webassembly.js", scripts);
        foreach (var script in scripts)
        {
            var response = await client.GetAsync(new Uri(script, UriKind.Relative));
            Assert.True(response.IsSuccessStatusCode, $"{script}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task The_app_files_are_served()
    {
        var response = await client.GetAsync(new Uri("/styles.css", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }
}
