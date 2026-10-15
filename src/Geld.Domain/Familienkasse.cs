namespace Geld.Domain;

/// <summary>
/// What the Familienkasse publishes once a year: the Kindergeld rate and the payment dates.
/// This is the only file to update when new figures come out (see the README).
/// </summary>
public static class Familienkasse
{
    /// <summary>Kindergeld per child and month since 1 January 2026, the same for every child.</summary>
    public const decimal MonthlyPerChild = 259m;

    /// <summary>Where the payment dates come from; shown in the app when they run out.</summary>
    public const string ScheduleSource = "https://www.arbeitsagentur.de/familie-und-kinder/auszahlungstermine";

    // The Familienkasse pays in a staggered way by the last digit of the Kindergeldnummer (Endziffer).
    // Day of the month for Endziffer 0 to 9, as published at ScheduleSource (retrieved 2026-10-14).
    private static readonly (int Year, int Month, int[] Days)[] Schedule =
    [
        (2026, 10, [5, 6, 8, 9, 12, 13, 15, 16, 19, 20]),
        (2026, 11, [4, 5, 6, 10, 11, 12, 13, 16, 17, 20]),
        (2026, 12, [3, 4, 7, 8, 9, 10, 11, 14, 15, 16]),
        (2027, 1, [8, 11, 12, 14, 15, 18, 19, 22, 25, 26]),
        (2027, 2, [3, 5, 9, 11, 12, 15, 16, 17, 18, 19]),
        (2027, 3, [3, 4, 5, 8, 9, 10, 11, 15, 16, 19]),
        (2027, 4, [5, 8, 12, 13, 14, 15, 16, 20, 21, 22]),
        (2027, 5, [5, 10, 11, 12, 13, 14, 19, 24, 25, 26]),
        (2027, 6, [3, 8, 9, 10, 14, 15, 16, 17, 18, 22]),
        (2027, 7, [5, 6, 7, 9, 12, 13, 15, 16, 19, 20]),
        (2027, 8, [4, 6, 9, 10, 12, 16, 17, 18, 19, 20]),
        (2027, 9, [3, 7, 8, 10, 13, 15, 16, 17, 20, 21]),
        (2027, 10, [5, 6, 8, 11, 12, 13, 15, 18, 19, 20]),
        (2027, 11, [4, 5, 8, 10, 11, 12, 15, 16, 19, 22]),
        (2027, 12, [3, 6, 7, 8, 9, 10, 13, 14, 15, 16]),
    ];

    /// <summary>All published payment dates for an Endziffer, in order.</summary>
    public static IEnumerable<DateOnly> PaymentDates(int endDigit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(endDigit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endDigit, 9);
        return Schedule.Select(m => new DateOnly(m.Year, m.Month, m.Days[endDigit]));
    }

    /// <summary>The next payment from today on (today included: the money may still arrive today).</summary>
    /// <returns>null once the published dates run out.</returns>
    public static DateOnly? NextPayment(int endDigit, DateOnly today) =>
        PaymentDates(endDigit).Where(d => d >= today).Cast<DateOnly?>().FirstOrDefault();
}
