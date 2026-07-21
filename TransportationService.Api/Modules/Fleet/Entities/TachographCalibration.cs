using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

/// <summary>
/// A tachograph calibration record for a vehicle. Most technical fields are optional; only
/// the calibration and next-due dates drive the compliance status and expiry reminders.
/// The attachment follows the shared IFileStorageService storage-key architecture.
/// </summary>
public class TachographCalibration : AuditableTenantEntity
{
    public Guid VehicleId { get; set; }

    public string? TachographType { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }

    public DateOnly CalibrationDate { get; set; }
    public DateOnly NextCalibrationDue { get; set; }

    public string? Workshop { get; set; }
    public string? CertificateNumber { get; set; }
    public string? SealReference { get; set; }
    public int? OdometerKm { get; set; }
    public int? TyreCircumferenceMm { get; set; }

    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }

    public string? Notes { get; set; }
}
