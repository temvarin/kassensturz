namespace Geld.Domain.Kindergeld;

/// <summary>
/// What the pot accepts of what the user types, in one place: the pot keeps these rules, and the input forms ask them
/// before they hand anything over, so a form never refuses what the pot would take, nor the other way round.
/// </summary>
public static class KindergeldRules
{
    /// <summary>Money into or out of the pot: always a positive sum; the kind of entry gives the direction.</summary>
    public static bool IsAmount(decimal amount) => amount > 0;
}
