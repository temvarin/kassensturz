namespace Geld.App.Shell;

/// <param name="Cancel">Second button; none for a plain message.</param>
public sealed record MessageModal(string Message, string Ok, string? Cancel, bool Danger, TaskCompletionSource<bool> Result);

/// <summary>
/// In-app dialogs instead of confirm() and alert(), which some browsers and embedded views suppress, so that confirm()
/// silently returns false.
/// </summary>
public sealed class Dialogs
{
    public MessageModal? Current { get; private set; }

    public event Action? Changed;

    /// <returns>true for the primary button; false if cancelled or closed by the app.</returns>
    public Task<bool> Confirm(string message, string ok = "OK", string? cancel = null, bool danger = false)
    {
        Close();
        var result = new TaskCompletionSource<bool>();
        Current = new MessageModal(message, ok, cancel, danger, result);
        Changed?.Invoke();
        return result.Task;
    }

    public void Alert(string message) => _ = Confirm(message);

    public void Close(bool result = false)
    {
        if (Current is null) return;
        Current.Result.TrySetResult(result);
        Current = null;
        Changed?.Invoke();
    }
}
