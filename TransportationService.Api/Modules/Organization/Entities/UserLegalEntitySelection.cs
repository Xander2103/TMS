using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Organization.Entities;

/// <summary>
/// Per-user "active legal entity" UI preference (one row per user per tenant). Purely a
/// convenience default for pickers - every mutation still validates the explicit entity id
/// server-side, so this can never bypass tenant boundaries or permissions.
/// </summary>
public class UserLegalEntitySelection : AuditableTenantEntity
{
    public Guid UserId { get; set; }
    public Guid LegalEntityId { get; set; }
}
