namespace TransportationService.Api.Common.Models;

/// <summary>
/// B26: a whitelisted server-side sort for list endpoints (<c>?sort=key&amp;dir=asc|desc</c>). An
/// unknown key falls back to the list's default and an absent direction to the key's natural
/// one (newest first for dates/amounts, A→Z for text), so a stale or hand-edited link never fails.
/// </summary>
public readonly record struct ListSort(string Key, bool Descending)
{
    public static ListSort Parse(string? sort, string? dir, string defaultKey, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string>? descendingByDefault = null)
    {
        var key = string.IsNullOrWhiteSpace(sort) ? defaultKey : sort.Trim().ToLowerInvariant();
        if (!allowed.Contains(key)) key = defaultKey;
        var normalized = dir?.Trim().ToLowerInvariant();
        var descending = normalized switch
        {
            "desc" => true,
            "asc" => false,
            // Absent or invalid: the key's natural direction.
            _ => descendingByDefault?.Contains(key) ?? false,
        };
        return new ListSort(key, descending);
    }
}

public static class ListSortExtensions
{
    public static IOrderedQueryable<T> OrderBy<T, TKey>(this IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    public static IOrderedQueryable<T> ThenBy<T, TKey>(this IOrderedQueryable<T> query, System.Linq.Expressions.Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.ThenByDescending(key) : query.ThenBy(key);
}
