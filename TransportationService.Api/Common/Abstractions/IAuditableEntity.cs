namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// An entity whose creation and last modification are tracked automatically by
/// <see cref="Persistence.AuditingSaveChangesInterceptor"/>. Services must not set
/// these fields by hand - the interceptor is the single source of truth.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
    Guid? CreatedByUserId { get; set; }
    Guid? UpdatedByUserId { get; set; }
}
