namespace TransportationService.Api.Modules.Qualifications.Entities;

public class QualificationType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool RequiresExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class QualificationTypeCodes
{
    public const string DrivingLicenceB = "DrivingLicenceB";
    public const string DrivingLicenceC = "DrivingLicenceC";
    public const string DrivingLicenceCE = "DrivingLicenceCE";
    public const string Code95 = "Code95";
    public const string Adr = "ADR";
    public const string MedicalFitness = "MedicalFitness";
    public const string DriverCard = "DriverCard";
    public const string CraneCertificate = "CraneCertificate";
    public const string ForkliftCertificate = "ForkliftCertificate";
    public const string Alfapass = "Alfapass";
    public const string Other = "Other";
}
