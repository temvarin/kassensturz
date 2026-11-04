using System.Globalization;
using System.Text.RegularExpressions;

namespace Geld.App.Shell;

/// <summary>
/// Euro amounts the German way, "1.351,82 €". The app ships without culture data (InvariantGlobalization), so the
/// format is spelled out here.
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

    public static string WithSign(decimal amount) => (amount > 0 ? "+" : "") + Format(amount);

    /// <summary>
    /// Reads "12,50", "12.5" or "300 €": up to nine digits, then a comma or a dot and up to two decimals. The German
    /// iOS keypad types a comma, others a dot. Only the format is checked here; which amounts a field accepts is the
    /// domain's rule (KindergeldRules, BudgetRules).
    /// </summary>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        var trimmed = text?.Replace("€", "").Trim() ?? "";
        if (!AmountPattern().IsMatch(trimmed)) return false;
        amount = decimal.Parse(trimmed.Replace(',', '.'), CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>"12,50", as it goes back into an input field.</summary>
    public static string ToInput(decimal amount) => amount.ToString("0.00", German);

    [GeneratedRegex(@"^\d{1,9}([.,]\d{1,2})?$")]
    private static partial Regex AmountPattern();
}
