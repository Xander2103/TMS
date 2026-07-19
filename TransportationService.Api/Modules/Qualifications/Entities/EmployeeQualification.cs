namespace TransportationService.Api.Modules.Qualifications.Entities;

public class EmployeeQualification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid QualificationTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly ObtainedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>ISO alpha-2 code of the issuing country (e.g. driving licence issued in "BE").</summary>
    public string? IssuingCountryCode { get; set; }
    public QualificationStatus Status { get; set; }
    public string? DocumentPath { get; set; }
    public string? Notes { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
