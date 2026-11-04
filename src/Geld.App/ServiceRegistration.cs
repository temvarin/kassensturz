using Geld.App.Backup;
using Geld.App.Budgeting;
using Geld.App.Kindergeld;
using Geld.App.Sync;
using Geld.Domain.Storage;

namespace Geld.App;

/// <summary>The composition root: the one place that knows every module and puts them together.</summary>
public static class ServiceRegistration
{
    /// <summary>Everything the components use, except the <see cref="HttpClient"/>, which tests and Program.cs give.</summary>
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
        // Modules
        .AddSingleton<KindergeldService>()
        .AddSingleton<BudgetService>()
        .AddSingleton<BackupService>()
        .AddSingleton<AccountService>()
        .AddSingleton<SyncService>()
        // Banners from other modules at the top of the Kindergeld and budget pages
        .AddSingleton(new PageBanners([
            PageBanners.Of<SignInBanner>(), PageBanners.Of<UnreadableBanner>(), PageBanners.Of<BackupBanner>()]));
}
