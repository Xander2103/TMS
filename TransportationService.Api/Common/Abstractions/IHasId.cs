namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// Marks an entity with a surrogate primary key, so generic guards (e.g.
/// <see cref="Persistence.TenantReferenceGuard"/>) can look entities up by id without reflection.
/// </summary>
public interface IHasId
{
    Guid Id { get; set; }
}
