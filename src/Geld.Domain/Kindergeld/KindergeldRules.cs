namespace Geld.Domain.Kindergeld;

/// <summary>What the pot accepts. The pot enforces these rules and the forms ask them, so the two never disagree.</summary>
public static class KindergeldRules
{
    /// <summary>Always positive; the kind of entry gives the direction.</summary>
    public static bool IsAmount(decimal amount) => amount > 0;
}
