using Geld.Server.Account;
using Geld.Server.Hosting;
using Geld.Server.Sync;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFamilySignIn();
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
builder.Services.AddSingleton(TimeProvider.System);
#pragma warning restore RS0030
// The connection string has no password, because the Web App logs in to Azure SQL with its managed identity.
builder.Services.AddSingleton<IDocumentStore>(services => new DocumentStore(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Geld")
        ?? throw new InvalidOperationException("The connection string Geld is missing.")));
var app = builder.Build();

app.UseSecurityHeaders();
// After the security headers, so that 401 and 403 answers carry them too.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapWhoAmI();
// Everything else under /api is for members only, endpoints added later included. Easy Auth lets everyone through,
// because the app must load without a sign-in, so this is the gate.
var api = app.MapGroup("/api").RequireAuthorization(Members.Policy);
// Shows that the database answers, and how often the shared pot has been saved.
api.MapGet("/status", async (IDocumentStore documents, CancellationToken cancellationToken) =>
    new { KindergeldVersion = (await documents.GetAsync("kindergeld", cancellationToken))?.Version });
api.MapPost("/sync", SyncEndpoint.Handle);
// Unknown API routes are errors, not app pages.
api.Map("{**path}", () => Results.NotFound());

// Routes like /budget exist only inside the app. The start page answers them and is never cached, so that a new
// release is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

/// <summary>Entry point; public for the tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
