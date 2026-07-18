namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// Marks an entity that belongs to a single tenant. Every query that reads such
/// an entity must be filtered by <see cref="TenantId"/> to preserve tenant isolation.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
