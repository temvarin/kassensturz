using Geld.Domain.Kindergeld;

namespace Geld.App.Kindergeld;

/// <summary>The pages' way into the pot: the pot to show, and one method per thing the user does.</summary>
public sealed class KindergeldService(AppState state, TimeProvider clock)
{
    public IReadOnlyKindergeldPot Pot => state.Data.Kindergeld;

    /// <summary>One month of Kindergeld for all children, booked today with a single tap.</summary>
    public void ReceiveMonth() => Change(p => p.Receive(clock.Today(), p.QuickAmount));

    /// <param name="id">
    /// The entry being edited, or null for a new one. If the other phone deleted it while the sheet was open, it is
    /// added again, so what the user just typed isn't lost.
    /// </param>
    public void Book(Guid? id, EntryKind kind, DateOnly date, decimal amount, string? category, string? note) =>
        Change(p =>
        {
            if (p.Entries.FirstOrDefault(e => e.Id == id) is { } existing)
                p.Update(existing with { Date = date, Amount = amount, Category = category, Note = note });
            else if (kind == EntryKind.Payment) p.Receive(date, amount, note);
            else p.Spend(date, amount, category, note);
        });

    public void Remove(Guid id) => Change(p => p.Remove(id));

    public void Configure(int children, int? endDigit) => Change(p => p.Configure(children, endDigit));

    private void Change(Action<KindergeldPot> change) => state.Change(data => change(data.Kindergeld));
}
