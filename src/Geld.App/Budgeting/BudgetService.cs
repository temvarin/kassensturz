using Geld.Domain.Budgeting;

namespace Geld.App.Budgeting;

/// <summary>The pages' way into the monthly budget: the budget to show, and one method per thing the user does.</summary>
public sealed class BudgetService(Slot<Budget> budget, TimeProvider clock)
{
    /// <summary>For reading. Changes go through the methods here, which save them.</summary>
    public IReadOnlyBudget Budget => budget.Current;

    public void SetSalary(decimal netSalary) => budget.Change(b => b.SetSalary(netSalary));

    /// <summary>The Rücklage as the user sees it on their account today.</summary>
    public void SetReserve(decimal amount) => budget.Change(b => b.SetReserve(amount, clock.Today()));

    public void RemoveReserve() => budget.Change(b => b.RemoveReserve());

    /// <param name="id">The fixed cost being edited; null for a new one. Removed meanwhile by a sync: added anew.</param>
    public void SaveDeduction(Guid? id, string name, decimal amount, int day) => budget.Change(b =>
    {
        if (b.Deductions.FirstOrDefault(d => d.Id == id) is { } existing)
            b.UpdateDeduction(existing with { Name = name, Amount = amount, Day = day });
        else b.AddDeduction(name, amount, day);
    });

    public void RemoveDeduction(Guid id) => budget.Change(b => b.RemoveDeduction(id));

    /// <param name="id">The planned spending being edited; null for a new one. Removed meanwhile by a sync: added anew.</param>
    public void SavePlanned(Guid? id, int year, int month, string name, decimal amount) => budget.Change(b =>
    {
        if (b.Planned.FirstOrDefault(p => p.Id == id) is { } existing)
            b.UpdatePlanned(existing with { Name = name, Amount = amount });
        else b.Plan(year, month, name, amount);
    });

    public void RemovePlanned(Guid id) => budget.Change(b => b.RemovePlanned(id));
}
