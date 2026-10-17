using System.Globalization;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>
/// FL-H3: every code that reaches the fleet timeline is translated here — no English enum value or
/// property name is ever shown on a normal timeline line (the raw before/after stays in
/// <c>Technical</c>).
/// </summary>
public static class FleetHistoryLabels
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-BE");

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string Km(decimal km) => km.ToString("#,##0", Nl) + " km";

    public static string Kg(decimal kg) => kg.ToString("#,##0.##", Nl) + " kg";

    public static string Litres(decimal litres) => litres.ToString("#,##0.##", Nl) + " l";

    public static string Money(decimal amount) => "€ " + amount.ToString("#,##0.00", Nl);

    public static string Window(DateOnly start, DateOnly? end) =>
        end is { } e && e > start ? $"{Date(start)} t/m {Date(e)}" : Date(start);

    /// <summary>Operational status names as stored in audit JSON (enum names, or legacy ordinals).</summary>
    public static string OperationalStatus(string? raw) => raw switch
    {
        "Available" or "0" => "Beschikbaar",
        "InUse" or "1" => "In gebruik",
        "InMaintenance" or "2" => "In onderhoud",
        "OutOfService" or "3" => "Buiten dienst",
        null or "" => "onbekend",
        _ => "onbekend",
    };

    public static bool IsBlockingStatus(string? raw) =>
        raw is "InMaintenance" or "OutOfService" or "2" or "3";

    public static string DocumentType(FleetDocumentType type, string? customName) => type switch
    {
        FleetDocumentType.Registration => "Inschrijvingsbewijs",
        FleetDocumentType.Insurance => "Verzekering",
        FleetDocumentType.TechnicalInspection => "Technische keuring",
        FleetDocumentType.Conformity => "Conformiteitsattest",
        FleetDocumentType.CraneInspection => "Kraankeuring",
        FleetDocumentType.RefrigerationCertificate => "Koelcertificaat",
        FleetDocumentType.AdrCertificate => "ADR-certificaat",
        FleetDocumentType.LeasingContract => "Leasingcontract",
        FleetDocumentType.TachographCalibration => "Tachograafijking",
        _ => string.IsNullOrWhiteSpace(customName) ? "Document" : customName.Trim(),
    };

    public static string InspectionType(InspectionType type, string? customName) => type switch
    {
        Entities.InspectionType.VehicleInspection => "Technische keuring",
        Entities.InspectionType.TrailerInspection => "Keuring oplegger",
        Entities.InspectionType.CraneInspection => "Kraankeuring",
        _ => string.IsNullOrWhiteSpace(customName) ? "Keuring" : customName.Trim(),
    };

    public static string InspectionResult(string? raw) => raw switch
    {
        "Passed" or "0" => "goedgekeurd",
        "PassedWithRemarks" or "1" => "goedgekeurd met opmerkingen",
        "Failed" or "2" => "afgekeurd",
        _ => "uitgevoerd",
    };

    public static string MaintenanceType(MaintenanceType type, string? customName) => type switch
    {
        Entities.MaintenanceType.PeriodicService => "Periodiek onderhoud",
        Entities.MaintenanceType.Repair => "Herstelling",
        Entities.MaintenanceType.TireService => "Bandenservice",
        Entities.MaintenanceType.BrakeService => "Remmenservice",
        Entities.MaintenanceType.Revision => "Revisie",
        _ => string.IsNullOrWhiteSpace(customName) ? "Onderhoud" : customName.Trim(),
    };

    public static string DamageSeverity(string? raw) => raw switch
    {
        "Minor" or "0" => "Licht",
        "Moderate" or "1" => "Matig",
        "Severe" or "2" => "Zwaar",
        "TotalLoss" or "3" => "Totaal verlies",
        _ => "onbekend",
    };

    /// <summary>The FL-H4 rule: only heavy damage and total loss make an asset undeployable.</summary>
    public static bool IsBlockingSeverity(string? raw) => raw is "Severe" or "TotalLoss" or "2" or "3";

    public static bool IsResolvedDamageStatus(string? raw) => raw is "Repaired" or "Closed" or "3" or "4";

    /// <summary>Title for a damage status reached through an update.</summary>
    public static string DamageStatusTitle(string? raw) => raw switch
    {
        "Reported" or "0" => "Schade opnieuw als gemeld gemarkeerd",
        "UnderAssessment" or "1" => "Schade in beoordeling",
        "InRepair" or "2" => "Schade in herstelling",
        "Repaired" or "3" => "Schade hersteld",
        "Closed" or "4" => "Schade afgesloten",
        _ => "Schademelding bijgewerkt",
    };

    public static string TripStatus(string? raw) => raw switch
    {
        "Draft" or "0" => "concept",
        "Planned" or "1" => "gepland",
        "InProgress" or "2" => "onderweg",
        "Completed" or "3" => "afgerond",
        "Cancelled" or "4" => "geannuleerd",
        _ => "onbekend",
    };

    /// <summary>Dutch labels for vehicle/trailer properties that can appear in an "Updated" audit row.</summary>
    public static string? PropertyLabel(string property) => property switch
    {
        "LicensePlate" => "kenteken",
        "Vin" => "chassisnummer (VIN)",
        "Brand" => "merk",
        "Model" => "model",
        "Year" => "bouwjaar",
        "OdometerKm" => "kilometerstand",
        "PayloadKg" => "laadvermogen",
        "CapacityKg" => "laadvermogen",
        "GrossVehicleWeightKg" => "MTM",
        "RequiredLicenceCode" => "vereist rijbewijs",
        "HasCrane" => "kraan",
        "HasTailLift" => "laadklep",
        "HasRefrigeration" => "koeling",
        "AdrSuitable" => "ADR-geschikt",
        "AxleCount" => "aantal assen",
        "LoadingMeters" => "laadmeters",
        "FuelType" => "brandstof",
        "OwnershipType" => "eigendomsvorm",
        "IsActive" => "administratief actief",
        "Notes" => "notities",
        _ => null,
    };
}
