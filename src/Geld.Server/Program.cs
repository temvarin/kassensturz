using Geld.Server.Account;
using Geld.Server.Hosting;
using Geld.Server.Sync;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFamilySignIn();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030
// Azure SQL; the connection string has no password: the Web App logs in with its managed identity.
builder.Services.AddSingleton<IDocumentStore>(services => new DocumentStore(services.GetRequiredService<IConfiguration>().GetConnectionString("Geld")
    ?? throw new InvalidOperationException("The connection string Geld is missing.")));
var app = builder.Build();

app.UseSecurityHeaders();
// Placed explicitly after the protections, so that its answers (401, 403) carry them too.
app.UseAuthentication();
app.UseAuthorization();

// The Blazor app's files, served with their pre-compressed variants and caching headers.
app.MapStaticAssets();

app.MapWhoAmI();
// Everything else under /api is for members only, including endpoints added later: Easy Auth lets everyone through
// to this app (which must load without signing in), so this is the gate.
var api = app.MapGroup("/api").RequireAuthorization(Members.Policy);
// Shows that the database answers, and how often the shared pot has been saved.
api.MapGet("/status", async (IDocumentStore documents, CancellationToken cancellationToken) =>
    new { KindergeldVersion = (await documents.GetAsync("kindergeld", cancellationToken))?.Version });
// The phones' changes in, the current documents out.
api.MapPost("/sync", SyncEndpoint.Handle);
// Unknown API routes are errors, not app pages.
api.Map("{**path}", () => Results.NotFound());

// Routes like /budget exist only inside the app: answer them with its start page, which must never be cached
// so that a new release is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
