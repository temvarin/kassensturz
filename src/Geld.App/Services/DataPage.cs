using Microsoft.AspNetCore.Components;

namespace Geld.App.Services;

/// <summary>A page that shows the user's data, and shows it anew when a sync brings changes while it is open.</summary>
public abstract class DataPage : ComponentBase, IDisposable
{
    [Inject] protected AppState State { get; set; } = null!;

    private bool listening;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!listening) State.Changed += OnChanged; // here rather than in OnInitialized, which pages override
        listening = true;
        return base.SetParametersAsync(parameters);
    }

    /// <summary>Recomputes what the page derives from the data; the page renders anew afterwards.</summary>
    protected virtual void OnDataChanged() { }

    public void Dispose()
    {
        State.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }

    private void OnChanged() => _ = InvokeAsync(() =>
    {
        OnDataChanged();
        StateHasChanged();
    });
}
