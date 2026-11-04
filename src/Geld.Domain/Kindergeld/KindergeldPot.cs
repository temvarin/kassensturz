using Geld.Domain.SharedKernel;

namespace Geld.Domain.Kindergeld;

public enum EntryKind { Payment, Spending }

/// <summary>Money into the pot (a Kindergeld payment) or out of it (a spending for the children).</summary>
/// <param name="Amount">Always positive; <see cref="Kind"/> gives the direction.</param>
/// <param name="Category">Only for spendings.</param>
public sealed record Entry(Guid Id, EntryKind Kind, DateOnly Date, decimal Amount, string? Category, string? Note) : IItem;

public sealed record CategoryTotal(string Category, decimal Amount);

public sealed record MonthTotals(int Year, int Month, decimal In, decimal Out);

public interface IReadOnlyKindergeldPot
{
    int Children { get; }
    int? EndDigit { get; }
    IReadOnlyList<Entry> Entries { get; }
    decimal Received { get; }
    decimal Spent { get; }
    decimal Balance { get; }
    decimal QuickAmount { get; }
    DateOnly? NextPayment(DateOnly today);
    IReadOnlyList<string> CategorySuggestions();
    IReadOnlyList<CategoryTotal> SpendingByCategory(DateOnly first, DateOnly last);
    IReadOnlyList<MonthTotals> MonthlyTotals(DateOnly today, int months);
}

/// <summary>The Kindergeld as one pot for all children. Payments go in, spendings for the children go out.</summary>
public sealed class KindergeldPot : IReadOnlyKindergeldPot
{
    public const string DefaultCategory = "Sonstiges";

    /// <summary>Suggested when categorising a spending; categories already used are suggested too.</summary>
    public static readonly IReadOnlyList<string> PresetCategories =
        ["Kleidung", "Kita & Schule", "Freizeit & Kurse", "Spielzeug", "Gesundheit", "Sparen", DefaultCategory];

    public KindergeldPot()
    {
    }

    /// <summary>Restores a saved pot through the same rules as entering one.</summary>
    /// <param name="entries">Newest first, as <see cref="Entries"/>.</param>
    /// <exception cref="ArgumentException">Something breaks the rules, e.g. a negative amount or an id twice.</exception>
    public KindergeldPot(int children, int? endDigit, IEnumerable<Entry> entries)
    {
        Configure(children, endDigit);
        Entries = Sorted(entries.Valid(Valid));
    }

    public int Children { get; private set; } = 1;

    /// <summary>Last digit of the Kindergeldnummer, which decides the payment dates; null until the user enters it.</summary>
    public int? EndDigit { get; private set; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Entry> Entries { get; private set; } = [];

    public decimal Received => Total(EntryKind.Payment);
    public decimal Spent => Total(EntryKind.Spending);
    public decimal Balance => Received - Spent;

    /// <summary>One month of Kindergeld for all children.</summary>
    public decimal QuickAmount => Children * Familienkasse.MonthlyPerChild;

    public void Configure(int children, int? endDigit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(children, 1);
        if (endDigit is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(endDigit));
        (Children, EndDigit) = (children, endDigit);
    }

    public DateOnly? NextPayment(DateOnly today) => EndDigit is { } digit ? Familienkasse.NextPayment(digit, today) : null;

    public Entry Receive(DateOnly date, decimal amount, string? note = null) =>
        Add(new Entry(Guid.NewGuid(), EntryKind.Payment, date, amount, null, note));

    /// <param name="category">Blank means <see cref="DefaultCategory"/>.</param>
    public Entry Spend(DateOnly date, decimal amount, string? category, string? note = null) =>
        Add(new Entry(Guid.NewGuid(), EntryKind.Spending, date, amount, category, note));

    public void Update(Entry entry) => Entries = Sorted(Entries.Replacing(Valid(entry)));

    public void Remove(Guid id) => Entries = Entries.Without(id);

    /// <summary>The presets, then the user's own categories in alphabetical order.</summary>
    public IReadOnlyList<string> CategorySuggestions() =>
        [.. PresetCategories.Concat(Entries
            .Select(e => e.Category).OfType<string>()
            .Except(PresetCategories, StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))];

    /// <summary>Spending per category between two dates (both included), largest first.</summary>
    public IReadOnlyList<CategoryTotal> SpendingByCategory(DateOnly first, DateOnly last) =>
        [.. Entries
            .Where(e => e.Kind == EntryKind.Spending && e.Date >= first && e.Date <= last)
            .GroupBy(e => e.Category ?? DefaultCategory)
            .Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.Category, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Money in and out per month, for the given number of months up to and including today's, oldest first.</summary>
    public IReadOnlyList<MonthTotals> MonthlyTotals(DateOnly today, int months)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - months);
        return [.. Enumerable.Range(0, months).Select(i =>
        {
            var month = first.AddMonths(i);
            var inMonth = Entries.Where(e => e.Date.Year == month.Year && e.Date.Month == month.Month).ToList();
            return new MonthTotals(month.Year, month.Month,
                inMonth.Where(e => e.Kind == EntryKind.Payment).Sum(e => e.Amount),
                inMonth.Where(e => e.Kind == EntryKind.Spending).Sum(e => e.Amount));
        })];
    }

    /// <summary>An independent copy. The list can be shared, because entries are values and lists are replaced, never changed.</summary>
    public KindergeldPot Copy() => new() { Children = Children, EndDigit = EndDigit, Entries = Entries };

    private Entry Add(Entry entry)
    {
        entry = Valid(entry);
        Entries = Sorted([entry, .. Entries]);
        return entry;
    }

    private static Entry Valid(Entry entry)
    {
        if (!KindergeldRules.IsAmount(entry.Amount)) throw new ArgumentOutOfRangeException(nameof(entry), entry.Amount, "The amount must be positive.");
        if (!Enum.IsDefined(entry.Kind)) throw new ArgumentOutOfRangeException(nameof(entry), "Unknown kind.");
        return entry with
        {
            Category = entry.Kind == EntryKind.Spending ? entry.Category.Optional() ?? DefaultCategory : null,
            Note = entry.Note.Optional(),
        };
    }

    // A stable sort, so among entries of one day the newest stays on top (Add puts it first).
    private static IReadOnlyList<Entry> Sorted(IEnumerable<Entry> entries) => [.. entries.OrderByDescending(e => e.Date)];

    private decimal Total(EntryKind kind) => Entries.Where(e => e.Kind == kind).Sum(e => e.Amount);
}
