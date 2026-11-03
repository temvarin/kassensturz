using Geld.App.Backup;
using Geld.App.Budgeting;
using Geld.App.Kindergeld;
using Geld.App.Sync;
using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;
using Geld.Domain.Storage;

namespace Geld.App;

/// <summary>The composition root: the one place that knows every module and puts them together.</summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Everything the components use, except the <see cref="HttpClient"/> for the server's API. The clock is a service
    /// too, so tests can replace it.
    /// </summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services) => services
        // Shell
        .AddSingleton<IKeyValueStore, LocalStorage>()
        .AddSingleton<DataStore>()
        .AddSingleton<AppState>()
        .AddSingleton<Dialogs>()
        .AddSingleton<Browser>()
#pragma warning disable RS0030 // The composition root: the one place that picks the real clock.
        .AddSingleton(TimeProvider.System)
#pragma warning restore RS0030
        // Modules. Each business module's service gets only its own part of the data; backup and sync work on all of it.
        .AddSingleton(services => new Slot<KindergeldPot>(services.GetRequiredService<AppState>(), data => data.Kindergeld))
        .AddSingleton(services => new Slot<Budget>(services.GetRequiredService<AppState>(), data => data.Budget))
        .AddSingleton<KindergeldService>()
        .AddSingleton<BudgetService>()
        .AddSingleton<BackupService>()
        .AddSingleton<AccountService>()
        .AddSingleton<SyncService>()
        // What the Kindergeld and budget pages show at their top, from the modules that know about it.
        .AddSingleton(new PageBanners([
            PageBanners.Of<SignInBanner>(), PageBanners.Of<UnreadableBanner>(), PageBanners.Of<BackupBanner>()]));
}
