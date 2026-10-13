using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.OrderImport.Entities;

/// <summary>
/// A reusable Excel-order-import mapping profile: which spreadsheet column carries which
/// order field. The mapping lives in <see cref="MappingJson"/> so a new customer file layout
/// is configuration, never code — no customer-specific business logic exists in this module.
/// </summary>
public class OrderImportProfile : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// JSON mapping spec: <c>{"headerRows":1,"columns":{"customerReference":"A","unloadingCity":"M",...}}</c>.
    /// Column references are letters ("A") or 1-based indexes ("13"); every field is optional
    /// except an unloading city/location column.
    /// </summary>
    public string MappingJson { get; set; } = "{}";

    /// <summary>
    /// Optional customer binding: a bound profile ranks first for that customer's imports and
    /// is refused for another customer's import. Null = generic (usable for every customer).
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// JSON array of the sample file's header texts, in column order. Powers profile
    /// recognition on upload and lets the editor re-open a profile without a new sample file.
    /// </summary>
    public string? SourceHeadersJson { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One uploaded workbook run (dry or real). The checksum powers duplicate detection: a file
/// that was already fully processed is refused, but re-importing after fixes stays possible
/// because the (TenantId, Sha256) index is deliberately NOT unique.
/// </summary>
public class OrderImportBatch : AuditableTenantEntity
{
    public Guid ProfileId { get; set; }
    public Guid CustomerId { get; set; }
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the uploaded file bytes (hex, 64 chars).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Validated | Processed | Failed (string — appending is safe).</summary>
    public string Status { get; set; } = OrderImportBatchStatus.Validated;

    public int RowCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }

    /// <summary>True = "Enkel valideren": no orders were created for this batch.</summary>
    public bool DryRun { get; set; }
}

public static class OrderImportBatchStatus
{
    public const string Validated = "Validated";
    public const string Processed = "Processed";
    public const string Failed = "Failed";
}

/// <summary>One spreadsheet row's outcome within a batch (row isolation: errors never abort the batch).</summary>
public class OrderImportRow : AuditableTenantEntity
{
    public Guid BatchId { get; set; }

    /// <summary>1-based spreadsheet row number (header rows included in the count).</summary>
    public int RowNumber { get; set; }

    /// <summary>Created | Skipped | Error (string — appending is safe).</summary>
    public string Status { get; set; } = OrderImportRowStatus.Created;

    /// <summary>Dutch, user-facing row error/skip reason.</summary>
    public string? Error { get; set; }

    public Guid? CreatedTransportOrderId { get; set; }

    /// <summary>The row's customer reference, kept for the per-row duplicate check trail.</summary>
    public string? ExternalReference { get; set; }
}

public static class OrderImportRowStatus
{
    public const string Created = "Created";
    public const string Skipped = "Skipped";
    public const string Error = "Error";
}
