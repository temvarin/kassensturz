using Microsoft.AspNetCore.Components;

namespace Geld.App.Shell;

/// <summary>A component that shows the user's data and redraws when it changes, also through a sync.</summary>
public abstract class DataComponent : ComponentBase, IDisposable
{
    // Only to hear of changes. Pages read and change the data through their area's service.
    [Inject] private AppState State { get; set; } = null!;

    [Inject] private TimeProvider Clock { get; set; } = null!;

    /// <summary>Asked anew on every render, never stored: the app may stay open across midnight.</summary>
    protected DateOnly Today => Clock.Today();

    private bool listening;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!listening) State.Changed += OnChanged; // here rather than in OnInitialized, which pages override
        listening = true;
        return base.SetParametersAsync(parameters);
    }

    /// <summary>Recomputes what the page derives from the data, before it renders again.</summary>
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
