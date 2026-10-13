namespace TransportationService.Api.Common.Abstractions;

/// <summary>
/// Base class for tenant-owned master-data entities. Provides the identity, tenant
/// ownership, automatic audit stamps and soft-delete state shared by every master-data
/// table. Timestamp/user/soft-delete fields are managed centrally by the auditing
/// interceptor - never assign them from a service.
/// </summary>
public abstract class AuditableTenantEntity : ITenantOwned, IHasId, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
