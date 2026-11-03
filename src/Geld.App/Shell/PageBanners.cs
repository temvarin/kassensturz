using Microsoft.AspNetCore.Components;

namespace Geld.App.Shell;

/// <summary>
/// The banners a main page shows at its top, in order. They come from other modules (signed out, data kept aside, backup
/// due): the composition root registers them here, so a page shows them without depending on those modules.
/// </summary>
public sealed record PageBanners(IReadOnlyList<RenderFragment> Banners)
{
    /// <summary>A banner component; each decides by itself whether there is something to show.</summary>
    public static RenderFragment Of<TBanner>() where TBanner : IComponent => builder =>
    {
        builder.OpenComponent<TBanner>(0);
        builder.CloseComponent();
    };
}
