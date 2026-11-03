using Geld.Domain;
using Geld.Domain.Storage;

namespace Geld.App.Shell;

/// <summary>
/// The user's data for the running app. Every change goes through <see cref="Change"/>, which saves it and tells the
/// pages and the sync in the same step. Only the modules that work on all of the data (backup, sync) and the
/// composition root use this; every other module's service gets just its part, as a <see cref="Slot{T}"/>, and pages
/// go through the service of their area.
/// </summary>
public sealed class AppState(DataStore store)
{
    public AppData Data { get; private set; } = store.Load();

    /// <summary>After every change, also one a sync brought: pages show the new data, the sync sends the changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// The one way to change the data: the change, the save and the notice happen together, so no change is shown
    /// without being stored and synced. If <paramref name="change"/> throws (the domain refuses it), nothing is saved.
    /// </summary>
    public void Change(Action<AppData> change)
    {
        change(Data);
        store.Save(Data);
        Changed?.Invoke();
    }

    /// <summary>
    /// Lets pages show their data anew without a change, e.g. when the app comes back to the front on another day: what
    /// they show depends on today's date.
    /// </summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>Deletes all data on this device, the sync state included; whatever is on the server stays.</summary>
    public void Reset()
    {
        Data = new AppData();
        store.Save(Data);
        Changed?.Invoke();
    }
}
