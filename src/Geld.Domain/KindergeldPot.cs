using System.Text.Json.Serialization;

namespace Geld.Domain;

public enum EntryKind { Payment, Spending }

/// <summary>Money into the pot (a Kindergeld payment) or out of it (a spending for the children).</summary>
/// <param name="Amount">Always positive; <see cref="Kind"/> gives the direction.</param>
/// <param name="Category">Only for spendings.</param>
public sealed record Entry(Guid Id, EntryKind Kind, DateOnly Date, decimal Amount, string? Category, string? Note) : IItem;

public sealed record CategoryTotal(string Category, decimal Amount);

public sealed record MonthTotals(int Year, int Month, decimal In, decimal Out);

/// <summary>
/// The Kindergeld as one pot for all children: payments go in, spendings for the children go out.
/// Persisted as part of <see cref="AppData"/>.
/// </summary>
/// <remarks>State changes only through its methods; the internal setters exist for deserialization.</remarks>
public sealed class KindergeldPot
{
    public const string DefaultCategory = "Sonstiges";

    /// <summary>Suggested when categorising a spending; categories already used are suggested too.</summary>
    public static readonly IReadOnlyList<string> PresetCategories =
        ["Kleidung", "Kita & Schule", "Freizeit & Kurse", "Spielzeug", "Gesundheit", "Sparen", DefaultCategory];

    [JsonInclude] public int Children { get; internal set; } = 1;

    /// <summary>Last digit of the Kindergeldnummer; decides the payment dates. Unknown until the user enters it.</summary>
    [JsonInclude] public int? EndDigit { get; internal set; }

    /// <summary>Newest first.</summary>
    [JsonInclude] public IReadOnlyList<Entry> Entries { get; internal set; } = [];

    [JsonIgnore] public decimal Received => Total(EntryKind.Payment);
    [JsonIgnore] public decimal Spent => Total(EntryKind.Spending);
    [JsonIgnore] public decimal Balance => Received - Spent;

    /// <summary>One month of Kindergeld for all children.</summary>
    [JsonIgnore] public decimal QuickAmount => Children * Familienkasse.MonthlyPerChild;

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

    /// <summary>Replaces the entry with the same id, e.g. after correcting a typo.</summary>
    public void Update(Entry entry) => Entries = Sorted(Entries.Replacing(Valid(entry)));

    public void Remove(Guid id) => Entries = Entries.Without(id);

    /// <summary>What changed since <paramref name="before"/>, for the next sync.</summary>
    public KindergeldChanges ChangesSince(KindergeldPot before) => new(
        (Children, EndDigit) == (before.Children, before.EndDigit) ? null : new KindergeldSettings(Children, EndDigit),
        ItemChanges.Between(before.Entries, Entries));

    /// <summary>The settings and entries that <paramref name="touched"/> is about, as they are now.</summary>
    internal KindergeldChanges Now(KindergeldChanges touched) =>
        new(touched.Settings is null ? null : new KindergeldSettings(Children, EndDigit), touched.Entries.Now(Entries));

    /// <summary>Takes over changes made on another device. All or nothing: throws if any of them breaks the rules.</summary>
    public void Apply(KindergeldChanges changes)
    {
        var (kept, added) = changes.Entries.ApplyTo(Entries, Valid);
        if (changes.Settings is { } settings) Configure(settings.Children, settings.EndDigit);
        Entries = Sorted([.. added, .. kept]); // like Add: the newest lead within a day
    }

    /// <summary>Categories to suggest: the presets, then the user's own in alphabetical order.</summary>
    public IReadOnlyList<string> CategorySuggestions() =>
        [.. PresetCategories.Concat(Entries
            .Select(e => e.Category).OfType<string>()
            .Except(PresetCategories).Distinct().Order(StringComparer.CurrentCultureIgnoreCase))];

    /// <summary>Spending per category between two dates (both included), largest first.</summary>
    public IReadOnlyList<CategoryTotal> SpendingByCategory(DateOnly from, DateOnly to) =>
        [.. Entries
            .Where(e => e.Kind == EntryKind.Spending && e.Date >= from && e.Date <= to)
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

    private Entry Add(Entry entry)
    {
        entry = Valid(entry);
        Entries = Sorted([entry, .. Entries]);
        return entry;
    }

    private static Entry Valid(Entry entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.Amount);
        if (!Enum.IsDefined(entry.Kind)) throw new ArgumentOutOfRangeException(nameof(entry), "Unknown kind.");
        return entry with
        {
            Category = entry.Kind == EntryKind.Spending ? entry.Category.Optional() ?? DefaultCategory : null,
            Note = entry.Note.Optional(),
        };
    }

    // Stable: among entries of the same day, the one added last stays on top.
    private static IReadOnlyList<Entry> Sorted(IEnumerable<Entry> entries) => [.. entries.OrderByDescending(e => e.Date)];

    private decimal Total(EntryKind kind) => Entries.Where(e => e.Kind == kind).Sum(e => e.Amount);
}
