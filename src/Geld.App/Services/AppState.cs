using Geld.Domain;

namespace Geld.App.Services;

/// <summary>The user's data for the running app. Components change <see cref="Data"/>, then call <see cref="Save"/>.</summary>
public sealed class AppState(DataStore store)
{
    public AppData Data { get; private set; } = store.Load();

    public void Save() => store.Save(Data);

    /// <summary>Replaces all data, e.g. with an imported backup.</summary>
    public void Replace(AppData data)
    {
        Data = data;
        Save();
    }
}
