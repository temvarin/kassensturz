var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The Blazor app's files, served with their pre-compressed variants and caching headers.
app.MapStaticAssets();

// Unknown API routes are errors, not app pages.
app.Map("/api/{**path}", () => Results.NotFound());

// Routes like /budget exist only inside the app: answer them with its start page, which must never be cached
// so that a new release is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
