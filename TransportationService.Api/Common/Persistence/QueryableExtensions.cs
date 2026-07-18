using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;

namespace TransportationService.Api.Common.Persistence;

public static class QueryableExtensions
{
    /// <summary>
    /// Materialises a query into a <see cref="PagedResult{T}"/>, issuing a single count and a
    /// single page query. The <paramref name="projection"/> keeps the read as a narrow SELECT.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> query,
        PageRequest page,
        Expression<Func<TSource, TResult>> projection,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, totalCount, page.Page, page.PageSize);
    }
}
