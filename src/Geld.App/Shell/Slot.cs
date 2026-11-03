using Geld.Domain;

namespace Geld.App.Shell;

/// <summary>
/// One module's part of the data: all that its service can read and change, and nothing of the other modules'. The
/// composition root hands each service its slot. Changes go through <see cref="AppState.Change"/>, so they are saved
/// and announced like every other.
/// </summary>
/// <param name="part">
/// Where the part is in the data. Asked anew each time: a sync or a reset replaces the part, not its contents.
/// </param>
public sealed class Slot<T>(AppState state, Func<AppData, T> part) where T : class
{
    public T Current => part(state.Data);

    /// <summary>If <paramref name="change"/> throws (the domain refuses it), nothing is saved.</summary>
    public void Change(Action<T> change) => state.Change(data => change(part(data)));
}
