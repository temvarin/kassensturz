namespace Geld.App.Shell;

public static class ClockExtensions
{
    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
