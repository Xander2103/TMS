using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Packages.Entities;

public enum PackageBarcodeType
{
    Code128,
    QrCode,
    External,
}

/// <summary>
/// The authoritative barcode registry: every value a package has ever carried, active or
/// retired. Uniqueness per tenant is enforced by a database constraint; scanning a retired
/// value resolves to the package with a "replaced label" warning instead of failing silently.
/// Relabelling retires the old row and issues a new one — history is never erased.
/// </summary>
public class PackageBarcode : AuditableTenantEntity
{
    public Guid PackageId { get; set; }

    public string Value { get; set; } = string.Empty;
    public PackageBarcodeType Type { get; set; } = PackageBarcodeType.Code128;

    public bool IsActive { get; set; } = true;
    public DateTime? RetiredAt { get; set; }
    public Guid? RetiredByUserId { get; set; }
    public string? RetireReason { get; set; }
}
