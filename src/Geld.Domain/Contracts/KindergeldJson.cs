using Geld.Domain.Kindergeld;

namespace Geld.Domain.Contracts;

// The JSON of the Kindergeld pot: in localStorage, in backup files, in sync answers and in the database. Changing a
// shape here changes data that already exists (tests/Geld.Domain.Tests/Contracts/README.md).

public enum EntryKindJson { Payment, Spending }

public sealed record EntryJson(Guid Id, EntryKindJson Kind, DateOnly Date, decimal Amount, string? Category, string? Note)
{
    public static EntryJson From(Entry entry) => new(entry.Id, entry.Kind switch
    {
        EntryKind.Payment => EntryKindJson.Payment,
        EntryKind.Spending => EntryKindJson.Spending,
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "Unknown kind."),
    }, entry.Date, entry.Amount, entry.Category, entry.Note);

    public Entry ToDomain() => new(Id, Kind switch
    {
        EntryKindJson.Payment => EntryKind.Payment,
        EntryKindJson.Spending => EntryKind.Spending,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown kind."),
    }, Date, Amount, Category, Note);
}

public sealed record KindergeldJson
{
    public int Children { get; init; } = 1;
    public int? EndDigit { get; init; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<EntryJson> Entries { get; init; } = [];

    public static KindergeldJson From(KindergeldPot pot) =>
        new() { Children = pot.Children, EndDigit = pot.EndDigit, Entries = [.. pot.Entries.Select(EntryJson.From)] };

    /// <exception cref="ArgumentException">It breaks the pot's rules.</exception>
    public KindergeldPot ToDomain() => new(Children, EndDigit, Entries.Select(e => Json.Item(e).ToDomain()));
}

internal static class Json
{
    /// <summary>A list item, which JSON can give as null although the type says it can't be.</summary>
    public static T Item<T>(T? item) where T : class => item ?? throw new ArgumentException("An item of a list is missing (null).");
}
