using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Warehousing.Entities;

/// <summary>
/// Wave 5 §1: one storage stay of one handling unit (package) in a warehouse — the movement-
/// based storage clock. Opened by a Received scan / depot return, closed when the package
/// leaves (load scan) or is administratively gone. At most one OPEN stay per package.
/// Historical rows are frozen; corrections close + reopen, never rewrite.
/// </summary>
public class StorageStay : AuditableTenantEntity
{
    public Guid PackageId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? WarehouseLocationId { get; set; }

    public DateTime InAt { get; set; }
    public DateTime? OutAt { get; set; }

    /// <summary>Custody events that opened/closed the stay (traceability, FK-less).</summary>
    public Guid? InPackageEventId { get; set; }
    public Guid? OutPackageEventId { get; set; }
}
