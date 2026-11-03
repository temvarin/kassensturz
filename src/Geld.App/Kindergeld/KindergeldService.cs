using Geld.Domain.Kindergeld;

namespace Geld.App.Kindergeld;

/// <summary>The pages' way into the Kindergeld pot: the pot to show, and one method per thing the user does.</summary>
public sealed class KindergeldService(Slot<KindergeldPot> pot, TimeProvider clock)
{
    /// <summary>For reading. Changes go through the methods here, which save them.</summary>
    public IReadOnlyKindergeldPot Pot => pot.Current;

    /// <summary>One month of Kindergeld for all children, booked today: the common case is a single tap.</summary>
    public void ReceiveMonth() => pot.Change(p => p.Receive(clock.Today(), p.QuickAmount));

    /// <summary>Saves an entry from the sheet: a new one, or the edited entry <paramref name="id"/>.</summary>
    /// <param name="id">
    /// The entry being edited; null for a new one. If a sync removed it while it was open (deleted on the other phone),
    /// it comes back as a new entry: what the user just typed isn't thrown away.
    /// </param>
    public void Book(Guid? id, EntryKind kind, DateOnly date, decimal amount, string? category, string? note) =>
        pot.Change(p =>
        {
            if (p.Entries.FirstOrDefault(e => e.Id == id) is { } existing)
                p.Update(existing with { Date = date, Amount = amount, Category = category, Note = note });
            else if (kind == EntryKind.Payment) p.Receive(date, amount, note);
            else p.Spend(date, amount, category, note);
        });

    public void Remove(Guid id) => pot.Change(p => p.Remove(id));

    public void Configure(int children, int? endDigit) => pot.Change(p => p.Configure(children, endDigit));
}
