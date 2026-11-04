using System.Globalization;

namespace Geld.App.Shell;

public static class Css
{
    /// <summary>A width such as "42.5%", for bars.</summary>
    public static string Percent(decimal part, decimal whole) =>
        whole == 0 ? "0%" : $"{Math.Round(part / whole * 100, 2).ToString(CultureInfo.InvariantCulture)}%";
}
