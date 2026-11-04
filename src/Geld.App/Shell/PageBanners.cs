using Microsoft.AspNetCore.Components;

namespace Geld.App.Shell;

/// <summary>
/// The banners at the top of a main page. They come from other modules, so the composition root registers them here,
/// and a page can show them without depending on those modules.
/// </summary>
public sealed record PageBanners(IReadOnlyList<RenderFragment> Banners)
{
    /// <summary>Each banner decides by itself whether it has something to show.</summary>
    public static RenderFragment Of<TBanner>() where TBanner : IComponent => builder =>
    {
        builder.OpenComponent<TBanner>(0);
        builder.CloseComponent();
    };
}
