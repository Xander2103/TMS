namespace TransportationService.Api.Common.Models;

/// <summary>
/// Standard envelope for a page of results returned by list/search endpoints.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), 0, page, pageSize);
}

/// <summary>
/// Normalised paging request. Callers clamp raw query-string values through
/// <see cref="Of"/> so services never receive a non-positive page or an unbounded page size.
/// </summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static PageRequest Of(int? page, int? pageSize)
    {
        var normalizedPage = page is > 0 ? page.Value : 1;
        var normalizedSize = pageSize switch
        {
            > 0 and <= MaxPageSize => pageSize.Value,
            > MaxPageSize => MaxPageSize,
            _ => DefaultPageSize,
        };
        return new PageRequest(normalizedPage, normalizedSize);
    }

    public int Skip => (Page - 1) * PageSize;
}
