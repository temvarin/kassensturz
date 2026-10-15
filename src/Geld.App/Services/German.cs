namespace Geld.App.Services;

/// <summary>Dates in German. The app ships without culture data (InvariantGlobalization), so the names are spelled out.</summary>
public static class German
{
    private static readonly string[] Months =
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    public static string MonthName(int month) => Months[month - 1];

    /// <summary>"Okt", for narrow places such as chart labels.</summary>
    public static string MonthShort(int month) => Months[month - 1][..3];

    /// <summary>"Oktober 2026".</summary>
    public static string Month(int year, int month) => $"{MonthName(month)} {year}";

    /// <summary>"11. November".</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day}. {MonthName(date.Month)}";

    /// <summary>"12.10.2026", for lists.</summary>
    public static string Date(DateOnly date) => $"{date.Day:00}.{date.Month:00}.{date.Year}";

    /// <summary>"heute", "morgen", "in 28 Tagen" – or "gestern", "vor 3 Tagen" for the past.</summary>
    public static string Relative(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "heute",
        1 => "morgen",
        -1 => "gestern",
        > 1 and var days => $"in {days} Tagen",
        var days => $"vor {-days} Tagen",
    };

    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
