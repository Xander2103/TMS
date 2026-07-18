namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// An entity that is never physically removed. A <c>Remove</c> on such an entity is
/// transparently converted to a soft delete by
/// <see cref="Persistence.AuditingSaveChangesInterceptor"/>, and a global query filter
/// hides soft-deleted rows from ordinary reads.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    Guid? DeletedByUserId { get; set; }
}
