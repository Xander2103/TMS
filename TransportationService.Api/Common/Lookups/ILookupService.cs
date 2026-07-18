using TransportationService.Api.Common.Models;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// Generic tenant-scoped CRUD for a <see cref="LookupEntity"/>. Registered once as an open
/// generic (<c>ILookupService&lt;&gt;</c>) so every concrete lookup entity is served without
/// bespoke service code. Deletes are soft; every mutation is audited.
/// </summary>
public interface ILookupService<TEntity> where TEntity : LookupEntity, new()
{
    Task<PagedResult<LookupItemDto>> SearchAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Active items only, ordered for dropdowns.</summary>
    Task<IReadOnlyList<LookupOptionDto>> ListOptionsAsync(CancellationToken cancellationToken);

    Task<LookupItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<LookupOperationResult> CreateAsync(CreateLookupRequest request, CancellationToken cancellationToken);

    Task<LookupOperationResult> UpdateAsync(Guid id, UpdateLookupRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes the item. Returns false when it does not exist for the tenant.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
