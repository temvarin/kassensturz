using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Geld.App.Tests;

/// <summary>The app's real services, with the data in memory and a clock the test controls.</summary>
public abstract class AppTestContext : BunitContext
{
    /// <summary>Kindergeld for Endziffer 4 came on 12 October; the next comes on 11 November.</summary>
    protected static readonly DateOnly Today = new(2026, 10, 14);

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(Today, new TimeOnly(10, 0), TimeSpan.Zero));

    protected AppTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // focusing fields, asking for persistent storage
        Services.AddAppServices();
        Services.AddSingleton<IKeyValueStore, MemoryStore>();
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton(new HttpClient(Server.Network) { BaseAddress = new Uri("http://localhost/") });
    }

    /// <summary>The real server behind a network the test controls. Unless a test signs someone in, nobody is.</summary>
    protected AppServer Server { get; } = new();

    protected AppData Data => Services.GetRequiredService<AppState>().Data;
    protected Dialogs Dialogs => Services.GetRequiredService<Dialogs>();

    /// <summary>What a fresh start of the app would load.</summary>
    protected AppData Stored => Services.GetRequiredService<DataStore>().Load();

    /// <summary>
    /// Waits for what happens on the server's side. bUnit's WaitForAssertion checks again only when a component renders,
    /// and nothing has to render after the server saved.
    /// </summary>
    protected static void Eventually(Action assertion)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (Xunit.Sdk.XunitException) when (waited.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>The button with this text or accessible label.</summary>
    protected static IElement Button<T>(IRenderedComponent<T> cut, string text) where T : IComponent =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text || b.GetAttribute("aria-label") == text);

    protected override void Dispose(bool disposing)
    {
        if (disposing) Server.Dispose();
        base.Dispose(disposing);
    }

    private sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _data = [];
        public string? Read(string key) => _data.GetValueOrDefault(key);
        public void Write(string key, string value) => _data[key] = value;
        public void Remove(string key) => _data.Remove(key);
    }
}
