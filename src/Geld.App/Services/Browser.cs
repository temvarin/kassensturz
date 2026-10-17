using Microsoft.JSInterop;

namespace Geld.App.Services;

/// <summary>Browser features without a .NET counterpart (wwwroot/browser.js).</summary>
public sealed class Browser(IJSRuntime js)
{
    private Func<Task>? visible;

    /// <summary>
    /// Asks the browser to keep the app's storage even when the device runs low on space.
    /// Browsers decide on their own, without asking the user.
    /// </summary>
    /// <returns>Whether the storage is protected.</returns>
    public Task<bool> ProtectStorageAsync() => js.InvokeAsync<bool>("geld.protectStorage").AsTask();

    /// <summary>Hands a file to the phone's share sheet (to save it in Files or Drive), or downloads it on a computer.</summary>
    /// <returns>false if the user cancelled.</returns>
    public Task<bool> SaveFileAsync(string fileName, string content) =>
        js.InvokeAsync<bool>("geld.saveFile", fileName, content).AsTask();

    /// <summary>Calls <paramref name="callback"/> whenever the app comes back to the front.</summary>
    public async Task WhenVisibleAsync(Func<Task> callback)
    {
        visible = callback;
        await js.InvokeVoidAsync("geld.whenVisible", DotNetObjectReference.Create(this));
    }

    [JSInvokable]
    public Task OnVisible() => visible?.Invoke() ?? Task.CompletedTask;
}
