using Geld.Domain.SharedKernel;

namespace Geld.Domain.Budgeting;

/// <summary>
/// What the budget accepts of what the user types, in one place: the budget keeps these rules, and the input forms ask
/// them before they hand anything over, so a form never refuses what the budget would take, nor the other way round.
/// </summary>
public static class BudgetRules
{
    /// <summary>A fixed cost or a planned spending: a positive sum.</summary>
    public static bool IsAmount(decimal amount) => amount > 0;

    /// <summary>The net salary; 0 if there is none.</summary>
    public static bool IsSalary(decimal salary) => salary >= 0;

    /// <summary>The Rücklage; 0 once it is used up, which is something else than having none.</summary>
    public static bool IsReserve(decimal amount) => amount >= 0;

    /// <summary>What a fixed cost or a planned spending is for.</summary>
    public static bool IsName(string? name) => name.Optional() is not null;
}
