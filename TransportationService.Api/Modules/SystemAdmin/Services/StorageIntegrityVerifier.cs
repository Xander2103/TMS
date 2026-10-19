using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Storage;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Tasks.Entities;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>One DB column that holds storage keys, as "table.column" plus a streaming query.</summary>
public sealed record FileReferenceColumn(string Label, Func<TransportationDbContext, IAsyncEnumerable<string>> Keys);

/// <summary>Per-column outcome. Keys only — file contents are never opened.</summary>
public sealed record FileReferenceColumnResult(
    string Label, long References, long Present, long Missing, long Invalid, IReadOnlyList<string> MissingKeys);

public sealed record StorageIntegrityReport(IReadOnlyList<FileReferenceColumnResult> Columns)
{
    public long References => Columns.Sum(c => c.References);
    public long Missing => Columns.Sum(c => c.Missing);
    public long Invalid => Columns.Sum(c => c.Invalid);
}

/// <summary>
/// Operator tool (production readiness: "integrity tool missing"): compares every database file
/// reference with the durable storage root. Runs ONLY on demand — the CLI flag
/// <c>--verify-storage</c> (handled in Program.cs before the host starts) — never on health
/// requests, because it touches every row and every file reference. Rows are streamed per column
/// (AsNoTracking, string projection), so memory stays bounded whatever the table sizes. Soft-deleted
/// rows are excluded by the normal query filters; without a request there is no tenant filter, so
/// the scan covers all tenants.
/// </summary>
public static class StorageIntegrityVerifier
{
    public const string CommandLineFlag = "--verify-storage";
    public const string ListMissingFlag = "--list-missing";

    /// <summary>Cap on the keys kept per column for <c>--list-missing</c> (bounded output).</summary>
    public const int MissingKeySampleLimit = 200;

    /// <summary>The 20 file-reference columns (audit live/filerefs.sql); labels use the real table/column names.</summary>
    public static readonly IReadOnlyList<FileReferenceColumn> Columns =
    [
        Column<Absence>("absences.AttachmentPath", e => e.AttachmentPath),
        Column<DamageReport>("damage_reports.AttachmentPath", e => e.AttachmentPath),
        Column<EmployeeDocument>("employee_documents.StorageKey", e => e.StorageKey),
        Column<EmployeeQualification>("employee_qualifications.DocumentPath", e => e.DocumentPath),
        Column<ExceptionPhoto>("execution_exception_photos.StoragePath", e => e.StoragePath),
        Column<FleetDocument>("fleet_documents.DocumentPath", e => e.DocumentPath),
        Column<Inspection>("inspections.AttachmentPath", e => e.AttachmentPath),
        Column<InvoiceAttachment>("invoice_attachments.StorageKey", e => e.StorageKey),
        Column<Invoice>("invoices.SellerLogoStorageKey", e => e.SellerLogoStorageKey),
        Column<LeasingContract>("leasing_contracts.StorageKey", e => e.StorageKey),
        Column<LegalEntity>("legal_entities.LogoStorageKey", e => e.LogoStorageKey),
        Column<MaintenanceRecord>("maintenance_records.AttachmentPath", e => e.AttachmentPath),
        Column<TransportOrderDocument>("order_documents.DocumentPath", e => e.DocumentPath),
        Column<PeppolIncomingDocument>("peppol_incoming_documents.PayloadStorageKey", e => e.PayloadStorageKey),
        Column<PeppolTransmission>("peppol_transmissions.PayloadStorageKey", e => e.PayloadStorageKey),
        Column<PodPhoto>("pod_photos.StoragePath", e => e.StoragePath),
        Column<ProofOfDelivery>("proofs_of_delivery.SignaturePath", e => e.SignaturePath),
        Column<StopExecution>("stop_executions.PodPath", e => e.PodPath),
        Column<TachographCalibration>("tachograph_calibrations.StorageKey", e => e.StorageKey),
        Column<TaskAttachment>("task_attachments.StorageKey", e => e.StorageKey),
    ];

    private static FileReferenceColumn Column<TEntity>(
        string label, System.Linq.Expressions.Expression<Func<TEntity, string?>> selector)
        where TEntity : class =>
        new(label, db => db.Set<TEntity>().AsNoTracking()
            .Select(selector)
            .Where(key => key != null && key != "")
            .Select(key => key!)
            .AsAsyncEnumerable());

    public static async Task<StorageIntegrityReport> ScanAsync(
        TransportationDbContext dbContext, string storageRootPath, bool collectMissingKeys,
        CancellationToken cancellationToken)
    {
        var results = new List<FileReferenceColumnResult>();
        foreach (var column in Columns)
        {
            long references = 0, present = 0, missing = 0, invalid = 0;
            var missingKeys = new List<string>();
            await foreach (var key in column.Keys(dbContext).WithCancellation(cancellationToken))
            {
                references++;
                string fullPath;
                try
                {
                    fullPath = StorageRoot.ResolveKeyPath(storageRootPath, key);
                }
                catch (ArgumentException)
                {
                    invalid++;
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    present++;
                    continue;
                }

                missing++;
                if (collectMissingKeys && missingKeys.Count < MissingKeySampleLimit)
                {
                    missingKeys.Add(key);
                }
            }

            results.Add(new FileReferenceColumnResult(column.Label, references, present, missing, invalid, missingKeys));
        }

        return new StorageIntegrityReport(results);
    }

    /// <summary>
    /// CLI entry: prints a per-column table and returns the process exit code — 0 when every
    /// reference resolves to a file, 2 when any is missing or invalid, 1 when the scan itself failed.
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider services, TextWriter output, bool listMissing, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var storageRoot = scope.ServiceProvider.GetRequiredService<StorageRoot>();
            var dbContext = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();

            await output.WriteLineAsync($"Storage integrity check — root: {storageRoot.RootPath} ({storageRoot.Source})");
            var report = await ScanAsync(dbContext, storageRoot.RootPath, listMissing, cancellationToken);

            await output.WriteLineAsync($"{"column",-46} {"refs",8} {"present",8} {"missing",8} {"invalid",8}");
            foreach (var column in report.Columns)
            {
                await output.WriteLineAsync(
                    $"{column.Label,-46} {column.References,8} {column.Present,8} {column.Missing,8} {column.Invalid,8}");
            }

            await output.WriteLineAsync(
                $"TOTAL: {report.References} references, {report.Missing} missing, {report.Invalid} invalid.");

            if (listMissing)
            {
                foreach (var column in report.Columns.Where(c => c.MissingKeys.Count > 0))
                {
                    await output.WriteLineAsync($"-- missing in {column.Label} (max {MissingKeySampleLimit}):");
                    foreach (var key in column.MissingKeys)
                    {
                        await output.WriteLineAsync($"   {key}");
                    }
                }
            }

            return report.Missing + report.Invalid > 0 ? 2 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type + message only: never a connection string (Npgsql messages do not carry the password).
            await output.WriteLineAsync($"Storage integrity check FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
