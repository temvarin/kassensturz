using Geld.Domain.SharedKernel;

namespace Geld.Domain.Budgeting;

/// <summary>What the budget accepts. The budget enforces these rules and the forms ask them, so the two never disagree.</summary>
public static class BudgetRules
{
    /// <summary>A fixed cost or a planned spending.</summary>
    public static bool IsAmount(decimal amount) => amount > 0;

    public static bool IsSalary(decimal salary) => salary >= 0;

    /// <summary>0 means used up, which is not the same as having none.</summary>
    public static bool IsReserve(decimal amount) => amount >= 0;

    public static bool IsName(string? name) => name.Optional() is not null;
}
