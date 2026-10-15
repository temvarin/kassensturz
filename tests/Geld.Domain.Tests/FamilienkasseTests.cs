namespace Geld.Domain.Tests;

public class FamilienkasseTests
{
    [Fact]
    public void The_next_payment_is_later_this_month_if_it_is_still_to_come() =>
        Assert.Equal(new DateOnly(2026, 10, 12), Familienkasse.NextPayment(4, new DateOnly(2026, 10, 1)));

    [Fact]
    public void On_payment_day_the_payment_is_today() =>
        Assert.Equal(new DateOnly(2026, 10, 12), Familienkasse.NextPayment(4, new DateOnly(2026, 10, 12)));

    [Fact]
    public void After_payment_day_the_next_payment_is_next_month() =>
        Assert.Equal(new DateOnly(2026, 11, 11), Familienkasse.NextPayment(4, new DateOnly(2026, 10, 14)));

    [Fact]
    public void Past_the_published_dates_there_is_no_next_payment() =>
        Assert.Null(Familienkasse.NextPayment(9, new DateOnly(2027, 12, 17)));

    [Fact]
    public void The_endziffer_is_a_single_digit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Familienkasse.NextPayment(10, new DateOnly(2026, 10, 1)));

    /// <summary>Guards against typos when the table is updated: the Familienkasse pays on working days, digit 0 first.</summary>
    [Fact]
    public void Published_dates_are_working_days_in_order_of_the_endziffer()
    {
        var dates = Enumerable.Range(0, 10).Select(d => Familienkasse.PaymentDates(d).ToList()).ToList();

        Assert.All(dates.SelectMany(d => d), date => Assert.True(
            date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{date} is a weekend day"));
        for (var month = 0; month < dates[0].Count; month++)
        {
            var inMonth = dates.Select(d => d[month]).ToList();
            Assert.Equal(inMonth.Order(), inMonth);
            Assert.Single(inMonth.Select(d => (d.Year, d.Month)).Distinct());
        }
    }
}
