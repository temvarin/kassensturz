namespace Geld.Domain.SharedKernel;

/// <summary>Something the user entered and can later change or delete.</summary>
public interface IItem
{
    Guid Id { get; }
}

internal static class ItemListExtensions
{
    public static IReadOnlyList<T> Replacing<T>(this IReadOnlyList<T> items, T item) where T : IItem =>
        items.Any(i => i.Id == item.Id)
            ? [.. items.Select(i => i.Id == item.Id ? item : i)]
            : throw new ArgumentException($"No item with id {item.Id}.", nameof(item));

    public static IReadOnlyList<T> Without<T>(this IReadOnlyList<T> items, Guid id) where T : IItem =>
        [.. items.Where(i => i.Id != id)];

    public static string Required(this string text, string paramName) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("Must not be empty.", paramName) : text.Trim();

    public static string? Optional(this string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Each item checked by <paramref name="valid"/>, which throws for one that breaks the rules.</summary>
    /// <exception cref="ArgumentException">An item is missing (null), or an id appears twice.</exception>
    public static IReadOnlyList<T> Valid<T>(this IEnumerable<T> items, Func<T, T> valid) where T : IItem
    {
        IReadOnlyList<T> checkedItems = [.. items.Select(i => valid(i ?? throw new ArgumentNullException(nameof(items))))];
        return checkedItems.DistinctBy(i => i.Id).Count() == checkedItems.Count
            ? checkedItems
            : throw new ArgumentException("An id appears twice.", nameof(items));
    }
}
