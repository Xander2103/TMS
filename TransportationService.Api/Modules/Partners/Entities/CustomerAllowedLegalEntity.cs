using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>
/// Wave 2 (spec Part O): the issuing entities this customer may be invoiced from. An EMPTY
/// set means every active entity is allowed (backward-compatible default — existing tenants
/// need no data migration). When non-empty: the customer default must be in the set, and
/// dossier/order/invoice entity choices outside it are validation errors; moving a dossier
/// to a non-default allowed entity requires dossiers.override_entity + reason (audited).
/// </summary>
public class CustomerAllowedLegalEntity : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid LegalEntityId { get; set; }
}
