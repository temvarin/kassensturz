using System.Globalization;
using System.Text.RegularExpressions;

namespace Geld.Domain;

/// <summary>
/// Euro amounts the German way: "1.351,82 €". The app ships without culture data (InvariantGlobalization),
/// so the format is spelled out here instead of coming from a de-DE culture.
/// </summary>
public static partial class Euro
{
    private static readonly NumberFormatInfo German = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NegativeSign = "−", // U+2212, as wide as the plus sign
    };

    public static string Format(decimal amount) => amount.ToString("#,##0.00", German) + " €";

    /// <summary>With an explicit sign, for money moving in (+) or out (−).</summary>
    public static string WithSign(decimal amount) => (amount > 0 ? "+" : "") + Format(amount);

    /// <summary>
    /// Reads a typed amount: digits with a comma or a dot before at most two decimals ("12,50", "12.5", "300").
    /// The German keypad on iOS types a comma, others a dot. A trailing "€" is allowed.
    /// </summary>
    /// <returns>false for anything else, and for zero: every amount in the app is a positive sum of money.</returns>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        var trimmed = text?.Replace("€", "").Trim() ?? "";
        if (!AmountPattern().IsMatch(trimmed)) return false;
        amount = decimal.Parse(trimmed.Replace(',', '.'), CultureInfo.InvariantCulture);
        return amount > 0;
    }

    /// <summary>The amount as it is typed back into an input field: "12,50".</summary>
    public static string ToInput(decimal amount) => amount.ToString("0.00", German);

    [GeneratedRegex(@"^\d{1,9}([.,]\d{1,2})?$")]
    private static partial Regex AmountPattern();
}
