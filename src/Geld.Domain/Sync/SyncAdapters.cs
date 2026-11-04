using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;

namespace Geld.Domain.Sync;

// The sync's adapters to the pot and the budget: the sync knows them, they know nothing of the sync. Each has the same
// three operations:
// - Diff: what changed from one state of the document to another;
// - Now: the items a set of changes touched, as they are in the document now;
// - Apply: the document with another device's changes taken over. It builds a new document by the same rules as
//   entering data, so a change that breaks one throws and leaves the old document as it was.

public static class KindergeldSync
{
    public static KindergeldChanges Diff(IReadOnlyKindergeldPot before, IReadOnlyKindergeldPot after) => new(
        Settings(after) == Settings(before) ? null : Settings(after),
        ItemChanges.Between(before.Entries, after.Entries));

    public static KindergeldChanges Now(IReadOnlyKindergeldPot pot, KindergeldChanges touched) => new(
        touched.Settings is null ? null : Settings(pot),
        touched.Entries.Now(pot.Entries));

    /// <exception cref="ArgumentException">A change breaks the pot's rules.</exception>
    public static KindergeldPot Apply(IReadOnlyKindergeldPot pot, KindergeldChanges changes)
    {
        var settings = changes.Settings ?? Settings(pot);
        var (kept, added) = changes.Entries.ApplyTo(pot.Entries);
        return new(settings.Children, settings.EndDigit, [.. added, .. kept]); // like Receive: the newest lead within a day
    }

    private static KindergeldSettings Settings(IReadOnlyKindergeldPot pot) => new(pot.Children, pot.EndDigit);
}

public static class BudgetSync
{
    public static BudgetChanges Diff(IReadOnlyBudget before, IReadOnlyBudget after) => new(
        Settings(after) == Settings(before) ? null : Settings(after),
        ItemChanges.Between(before.Deductions, after.Deductions),
        ItemChanges.Between(before.Planned, after.Planned));

    public static BudgetChanges Now(IReadOnlyBudget budget, BudgetChanges touched) => new(
        touched.Settings is null ? null : Settings(budget),
        touched.Deductions.Now(budget.Deductions),
        touched.Planned.Now(budget.Planned));

    /// <exception cref="ArgumentException">A change breaks the budget's rules.</exception>
    public static Budget Apply(IReadOnlyBudget budget, BudgetChanges changes)
    {
        var settings = changes.Settings ?? Settings(budget);
        var deductions = changes.Deductions.ApplyTo(budget.Deductions);
        var planned = changes.Planned.ApplyTo(budget.Planned);
        return new(settings.NetSalary,
            [.. deductions.Kept, .. deductions.Added], // the budget sorts them by the day they are debited
            [.. planned.Kept, .. planned.Added],
            settings.Reserve);
    }

    private static BudgetSettings Settings(IReadOnlyBudget budget) => new(budget.NetSalary, budget.Reserve);
}
