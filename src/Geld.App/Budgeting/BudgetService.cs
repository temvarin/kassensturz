using Geld.Domain.Budgeting;

namespace Geld.App.Budgeting;

/// <summary>The pages' way into the budget: the budget to show, and one method per thing the user does.</summary>
public sealed class BudgetService(AppState state, TimeProvider clock)
{
    public IReadOnlyBudget Budget => state.Data.Budget;

    public void SetSalary(decimal netSalary) => Change(b => b.SetSalary(netSalary));

    /// <summary>The Rücklage as the user sees it on their bank account today.</summary>
    public void SetReserve(decimal amount) => Change(b => b.SetReserve(amount, clock.Today()));

    public void RemoveReserve() => Change(b => b.RemoveReserve());

    /// <param name="id">The fixed cost being edited, or null. If a sync removed it meanwhile, it is added again.</param>
    public void SaveDeduction(Guid? id, string name, decimal amount, Schedule schedule) => Change(b =>
    {
        if (b.Deductions.FirstOrDefault(d => d.Id == id) is { } existing)
            b.UpdateDeduction(existing with { Name = name, Amount = amount, Schedule = schedule });
        else b.AddDeduction(name, amount, schedule);
    });

    public void RemoveDeduction(Guid id) => Change(b => b.RemoveDeduction(id));

    /// <param name="id">The planned spending being edited, or null. If a sync removed it meanwhile, it is added again.</param>
    public void SavePlanned(Guid? id, int year, int month, string name, decimal amount) => Change(b =>
    {
        if (b.Planned.FirstOrDefault(p => p.Id == id) is { } existing)
            b.UpdatePlanned(existing with { Name = name, Amount = amount });
        else b.Plan(year, month, name, amount);
    });

    public void RemovePlanned(Guid id) => Change(b => b.RemovePlanned(id));

    private void Change(Action<Budget> change) => state.Change(data => change(data.Budget));
}
