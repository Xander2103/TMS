using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>Category of an employee document (drives sensitivity and default expiry policy).</summary>
public enum EmployeeDocumentCategory
{
    IdentityCardFront,
    IdentityCardBack,
    DrivingLicenceFront,
    DrivingLicenceBack,
    EmploymentDocument,
    MedicalDocument,
    Certificate,
    Contract,
    AdditionalDocument,
    Other,
}

/// <summary>
/// A stored file belonging to an employee (ID scans, contracts, medical documents, ...).
/// Distinct from qualification attachments. Content lives in IFileStorageService (category
/// "employee-documents"); this row is metadata. Sensitive categories (ID/medical/contract)
/// need the extra employee_documents.view_sensitive permission to list or download.
/// </summary>
public class EmployeeDocument : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }
    public EmployeeDocumentCategory Category { get; set; }

    /// <summary>Free label when Category is Other/AdditionalDocument.</summary>
    public string? CustomLabel { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
}
