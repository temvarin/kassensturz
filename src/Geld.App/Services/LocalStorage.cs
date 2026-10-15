using Geld.Domain;
using Microsoft.JSInterop;

namespace Geld.App.Services;

/// <summary>The browser's localStorage. Called synchronously: in WebAssembly, JavaScript runs in the same thread.</summary>
internal sealed class LocalStorage(IJSRuntime js) : IKeyValueStore
{
    private IJSInProcessRuntime Js => (IJSInProcessRuntime)js;

    public string? Read(string key) => Js.Invoke<string?>("localStorage.getItem", key);

    public void Write(string key, string value) => Js.InvokeVoid("localStorage.setItem", key, value);
}
