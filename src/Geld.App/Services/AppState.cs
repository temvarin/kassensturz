using Geld.Domain;

namespace Geld.App.Services;

/// <summary>The user's data for the running app. Components change <see cref="Data"/>, then call <see cref="Save"/>.</summary>
public sealed class AppState(DataStore store)
{
    public AppData Data { get; private set; } = store.Load();

    /// <summary>After every save, also of data a sync brought: pages show the new data, the sync sends the changes.</summary>
    public event Action? Changed;

    public void Save()
    {
        store.Save(Data);
        Changed?.Invoke();
    }

    /// <summary>Replaces all data with an imported backup.</summary>
    public void Replace(AppData backup)
    {
        Data.RestoreFrom(backup);
        Save();
    }

    /// <summary>Deletes all data on this device, the sync state included; whatever is on the server stays.</summary>
    public void Reset()
    {
        Data = new AppData();
        Save();
    }
}
