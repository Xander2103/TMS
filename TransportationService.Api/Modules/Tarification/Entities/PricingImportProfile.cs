using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A reusable column mapping for pricing imports (sprint 4D). Real customer rate sheets use
/// their own header names ("Atlas Copco 2026", "Nexans 2026", …); the profile records which
/// source header feeds which pricing field, so the same file layout can be re-imported next
/// year without touching code. This is CONFIGURATION — never a customer-specific importer.
/// </summary>
public class PricingImportProfile : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional note, e.g. which contact at the customer sends this layout.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// JSON object: canonical field key → the header text in the source workbook.
    /// Fields left out fall back to the standard header, so a profile only has to describe
    /// what actually differs.
    /// </summary>
    public string MappingJson { get; set; } = "{}";

    /// <summary>Header row number (1-based); some customer sheets carry a title above the table.</summary>
    public int HeaderRow { get; set; } = 1;

    /// <summary>Worksheet name to read; null = the "Tarieven" sheet, else the first one.</summary>
    public string? SheetName { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One pricing-import attempt (sprint 4F). Kept for traceability — who loaded which file into
/// which table, and what it did — and to recognise a re-import of the exact same file.
/// </summary>
public class PricingImportRun : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }

    /// <summary>The agreement the rows actually landed in (differs when a new version was created).</summary>
    public Guid TargetAgreementId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the uploaded bytes; identical content = identical checksum.</summary>
    public string Checksum { get; set; } = string.Empty;

    public Guid? ProfileId { get; set; }
    public string? ProfileName { get; set; }

    public int RowsRead { get; set; }
    public int RowsValid { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Failed { get; set; }

    /// <summary>Import mode used (update in place vs. duplicate as a new version).</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// Outcome of the attempt: Succeeded (rows written), Rejected (validation refused the file,
    /// nothing written) or Failed (the database write itself failed and was rolled back).
    /// </summary>
    public string Status { get; set; } = PricingImportRunStatus.Succeeded;

    /// <summary>The rejection/failure message, so the history explains WHY nothing landed.</summary>
    public string? Error { get; set; }
}

public static class PricingImportRunStatus
{
    public const string Succeeded = "Succeeded";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}
