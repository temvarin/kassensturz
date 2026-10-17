using Geld.Domain;

namespace Geld.App.Services;

public static class ServiceRegistration
{
    /// <summary>
    /// Everything the components use, except the <see cref="HttpClient"/> for the server's API. The clock is a service
    /// too, so tests can replace it.
    /// </summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services) => services
        .AddSingleton<IKeyValueStore, LocalStorage>()
        .AddSingleton<DataStore>()
        .AddSingleton<AppState>()
        .AddSingleton<SyncService>()
        .AddSingleton<Dialogs>()
        .AddSingleton<Browser>()
        .AddSingleton(TimeProvider.System);
}
