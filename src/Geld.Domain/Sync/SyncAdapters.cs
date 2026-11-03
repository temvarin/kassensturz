using Geld.Domain.Budgeting;
using Geld.Domain.Kindergeld;

namespace Geld.Domain.Sync;

/// <summary>
/// What the sync needs of a synced document. The sync is the mediator between the modules: it knows the pot and the
/// budget through their adapters, and they know nothing of it. <see cref="Replica{TDocument, TChanges, TAdapter}"/>
/// runs the sync of one document on these alone.
/// </summary>
public interface ISyncAdapter<TDocument, TChanges>
{
    /// <summary>The document as it is before anything was entered: the base of a device that never synced.</summary>
    static abstract TDocument Empty();

    /// <summary>An independent copy: the document as it was when a sync started, while the user goes on changing it.</summary>
    static abstract TDocument Copy(TDocument document);

    /// <summary>What changed from <paramref name="before"/> to <paramref name="after"/>, for the next sync.</summary>
    static abstract TChanges Diff(TDocument before, TDocument after);

    /// <summary>What <paramref name="touched"/> is about, as it is now in <paramref name="document"/>.</summary>
    static abstract TChanges Now(TDocument document, TChanges touched);

    /// <summary>
    /// The document with changes from another device taken over. A new document, built by the same rules as entering
    /// it: if a change breaks one, this throws, and <paramref name="document"/> is unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">A change breaks the document's rules.</exception>
    static abstract TDocument Apply(TDocument document, TChanges changes);

    static abstract bool IsEmpty(TChanges changes);

    /// <summary>Both changes together; for an item in both, <paramref name="first"/>'s.</summary>
    static abstract TChanges Union(TChanges first, TChanges second);
}

public sealed class KindergeldSync : ISyncAdapter<KindergeldPot, KindergeldChanges>
{
    private KindergeldSync()
    {
    }

    public static KindergeldPot Empty() => new();

    public static KindergeldPot Copy(KindergeldPot pot) => pot.Copy();

    public static bool IsEmpty(KindergeldChanges changes) => changes.IsEmpty;

    public static KindergeldChanges Union(KindergeldChanges first, KindergeldChanges second) => first.Union(second);

    public static KindergeldChanges Diff(KindergeldPot before, KindergeldPot after) => new(
        (after.Children, after.EndDigit) == (before.Children, before.EndDigit) ? null : new(after.Children, after.EndDigit),
        ItemChanges.Between(before.Entries, after.Entries));

    public static KindergeldChanges Now(KindergeldPot pot, KindergeldChanges touched) =>
        new(touched.Settings is null ? null : new(pot.Children, pot.EndDigit), touched.Entries.Now(pot.Entries));

    public static KindergeldPot Apply(KindergeldPot pot, KindergeldChanges changes)
    {
        var (kept, added) = changes.Entries.ApplyTo(pot.Entries);
        var settings = changes.Settings ?? new(pot.Children, pot.EndDigit);
        return new(settings.Children, settings.EndDigit, [.. added, .. kept]); // like Receive: the newest lead within a day
    }
}

public sealed class BudgetSync : ISyncAdapter<Budget, BudgetChanges>
{
    private BudgetSync()
    {
    }

    public static Budget Empty() => new();

    public static Budget Copy(Budget budget) => budget.Copy();

    public static bool IsEmpty(BudgetChanges changes) => changes.IsEmpty;

    public static BudgetChanges Union(BudgetChanges first, BudgetChanges second) => first.Union(second);

    public static BudgetChanges Diff(Budget before, Budget after) => new(
        Settings(after) == Settings(before) ? null : Settings(after),
        ItemChanges.Between(before.Deductions, after.Deductions),
        ItemChanges.Between(before.Planned, after.Planned));

    public static BudgetChanges Now(Budget budget, BudgetChanges touched) => new(
        touched.Settings is null ? null : Settings(budget),
        touched.Deductions.Now(budget.Deductions),
        touched.Planned.Now(budget.Planned));

    public static Budget Apply(Budget budget, BudgetChanges changes)
    {
        var settings = changes.Settings ?? Settings(budget);
        var deductions = changes.Deductions.ApplyTo(budget.Deductions);
        var planned = changes.Planned.ApplyTo(budget.Planned);
        return new(settings.NetSalary,
            [.. deductions.Kept, .. deductions.Added], // the budget puts them in the order they are debited
            [.. planned.Kept, .. planned.Added],
            settings.Reserve);
    }

    private static BudgetSettings Settings(Budget budget) => new(budget.NetSalary, budget.Reserve);
}
