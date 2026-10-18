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

    [Theory]
    [InlineData("/")]
    [InlineData("/styles.css")]
    [InlineData("/api/whoami")]
    public async Task Every_answer_tells_the_browser_to_run_only_this_site_s_code_and_never_in_a_frame(string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("script-src 'self' 'wasm-unsafe-eval';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task The_start_page_has_no_inline_script_which_the_policy_would_block()
    {
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.DoesNotMatch("<script(?![^>]*src=)", html);
    }

    [Fact]
    public async Task Answers_of_the_api_are_never_stored_in_a_cache()
    {
        var response = await client.GetAsync(new Uri("/api/whoami", UriKind.Relative));

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task The_app_files_are_served()
    {
        var response = await client.GetAsync(new Uri("/styles.css", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }
}
