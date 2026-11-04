using Geld.Domain;
using Geld.Domain.Storage;

namespace Geld.App.Shell;

/// <summary>The user's data while the app runs. Pages don't use it directly; they go through the service of their area.</summary>
public sealed class AppState(DataStore store)
{
    public AppData Data { get; private set; } = store.Load();

    /// <summary>After every change, including those a sync brings.</summary>
    public event Action? Changed;

    /// <summary>Changes, saves and announces the data in one step. If the domain refuses the change, nothing is saved.</summary>
    public void Change(Action<AppData> change)
    {
        change(Data);
        store.Save(Data);
        Changed?.Invoke();
    }

    /// <summary>Redraws the pages without a change, e.g. when the app comes back on another day.</summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>Deletes all data on this device, sync state included. The server keeps its copy.</summary>
    public void Reset()
    {
        Data = new AppData();
        store.Save(Data);
        Changed?.Invoke();
    }
}
