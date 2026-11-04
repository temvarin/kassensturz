using Microsoft.JSInterop;

namespace Geld.App.Shell;

/// <summary>Browser features without a .NET counterpart (wwwroot/browser.js).</summary>
public sealed class Browser(IJSRuntime js)
{
    private Func<Task>? visible;

    /// <summary>Asks the browser to keep the app's storage when space runs low. Most browsers decide on their own.</summary>
    /// <returns>Whether the storage is protected.</returns>
    public Task<bool> ProtectStorageAsync() => js.InvokeAsync<bool>("geld.protectStorage").AsTask();

    /// <summary>The share sheet on a phone (to save the file in Files or Drive), a download on a computer.</summary>
    /// <returns>false if the user cancelled.</returns>
    public Task<bool> SaveFileAsync(string fileName, string content) =>
        js.InvokeAsync<bool>("geld.saveFile", fileName, content).AsTask();

    /// <summary>One callback; a later call replaces it.</summary>
    public async Task WhenVisibleAsync(Func<Task> callback)
    {
        var listening = visible is not null;
        visible = callback;
        if (!listening) await js.InvokeVoidAsync("geld.whenVisible", DotNetObjectReference.Create(this));
    }

    [JSInvokable]
    public Task OnVisible() => visible?.Invoke() ?? Task.CompletedTask;
}
