using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.OrderImport.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.OrderImport.Services;

public sealed record OrderImportProfileDto(
    Guid Id, string Name, string? Description, string MappingJson, bool IsActive,
    /// <summary>Optional customer binding (null = generic profile, usable for every customer).</summary>
    Guid? CustomerId = null, string? CustomerName = null,
    /// <summary>Parsed mapping: field key → column reference, for the editor (MappingJson stays the wire truth).</summary>
    IReadOnlyDictionary<string, string>? Mapping = null,
    /// <summary>Sample-file headers in column order; lets the editor open without a new upload.</summary>
    IReadOnlyList<string>? SourceHeaders = null,
    int MappedFieldCount = 0, DateTime UpdatedAt = default);

public sealed record SaveOrderImportProfileRequest(
    string Name, string? Description, Guid? CustomerId, bool IsActive,
    int HeaderRows, IReadOnlyDictionary<string, string> Mapping,
    IReadOnlyList<string>? SourceHeaders = null);

public sealed record OrderImportFieldDto(string Key, string Group);

/// <summary>One sample-file column: header, a few example values and the deterministic suggestion.</summary>
public sealed record OrderImportColumnAnalysisDto(
    int ColumnIndex, string Header, IReadOnlyList<string> SampleValues,
    string? SuggestedField, int? Confidence);

/// <summary>How well a SAVED profile's stored headers match the uploaded file's headers.</summary>
public sealed record OrderImportProfileMatchDto(Guid ProfileId, string Name, Guid? CustomerId, int MatchPercent);

public sealed record OrderImportAnalysisDto(
    IReadOnlyList<OrderImportColumnAnalysisDto> Columns,
    IReadOnlyList<OrderImportProfileMatchDto> ProfileMatches);

public sealed record OrderImportBatchDto(
    Guid Id, Guid ProfileId, string ProfileName, Guid CustomerId, string CustomerName,
    string FileName, string Status, int RowCount, int SuccessCount, int FailureCount,
    bool DryRun, DateTime CreatedAt);

public sealed record OrderImportRowDto(
    int RowNumber, string Status, string? Error, Guid? CreatedTransportOrderId, string? ExternalReference);

public sealed record OrderImportBatchDetailDto(
    OrderImportBatchDto Batch, IReadOnlyList<OrderImportRowDto> Rows);

public interface IOrderImportService
{
    /// <summary>Profiles (active only unless includeInactive); lazily seeds the generic sample profile per tenant (add-if-missing).</summary>
    Task<IReadOnlyList<OrderImportProfileDto>> ListProfilesAsync(CancellationToken cancellationToken, bool includeInactive = false);

    Task<OrderImportProfileDto> CreateProfileAsync(SaveOrderImportProfileRequest request, CancellationToken cancellationToken);

    Task<OrderImportProfileDto?> UpdateProfileAsync(Guid id, SaveOrderImportProfileRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteProfileAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a sample workbook's header row + a few sample values per column, suggests target
    /// fields deterministically (alias catalog) and scores saved profiles against the headers.
    /// Never persists anything.
    /// </summary>
    Task<OrderImportAnalysisDto> AnalyzeAsync(byte[] fileBytes, CancellationToken cancellationToken);

    Task<PagedResult<OrderImportBatchDto>> ListBatchesAsync(int? page, int? pageSize, CancellationToken cancellationToken);

    Task<OrderImportBatchDetailDto?> GetBatchAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Parses the workbook through the profile's column mapping and either validates only
    /// (dryRun) or creates one transport order per valid row via the regular order service —
    /// numbering, wrapper dossier, pricing and audit come for free. Row errors never abort
    /// the batch (row isolation). Throws <see cref="DomainValidationException"/> for
    /// file-level problems (invalid workbook, already-processed checksum, unknown profile).
    /// </summary>
    Task<OrderImportBatchDetailDto> ImportAsync(
        Guid profileId, Guid customerId, string fileName, byte[] fileBytes, bool dryRun,
        CancellationToken cancellationToken);
}

/// <summary>
/// Automated Excel ORDER import (P13). Mirrors the EDI pipeline's guarantees (checksum
/// dedupe, per-row status, dry-run) and the customer-import parsing style (ClosedXML,
/// Dutch row messages), but goes through <see cref="ITransportOrderService.CreateAsync"/>
/// so every created order is a first-class order inside a dossier.
/// </summary>
public class OrderImportService : IOrderImportService
{
    private const int MaxRows = 1000;
    private const string SampleProfileName = "Generiek v1";
    private const string EntityType = "OrderImportBatch";

    private static readonly string[] KnownFields =
    [
        "customerReference", "orderDate", "goodsDescription", "quantity", "quantityUnit",
        "weightKg", "loadingLocation", "loadingPostalCode", "loadingCity", "loadingCountry",
        "unloadingLocation", "unloadingPostalCode", "unloadingCity", "unloadingCountry", "adr",
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ITransportOrderService _orderService;

    public OrderImportService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        ITransportOrderService orderService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _orderService = orderService;
    }

    // ------------------------------------------------------------- profiles

    public async Task<IReadOnlyList<OrderImportProfileDto>> ListProfilesAsync(
        CancellationToken cancellationToken, bool includeInactive = false)
    {
        await EnsureSampleProfileAsync(cancellationToken);

        var profiles = await _dbContext.OrderImportProfiles.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId && (includeInactive || p.IsActive))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // Fixed number of queries regardless of profile count.
        var customerIds = profiles.Where(p => p.CustomerId is not null).Select(p => p.CustomerId!.Value).Distinct().ToList();
        var customerNames = customerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == _tenantContext.TenantId && customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return profiles.Select(p => MapProfile(p, customerNames)).ToList();
    }

    private static OrderImportProfileDto MapProfile(OrderImportProfile profile, IReadOnlyDictionary<Guid, string> customerNames)
    {
        // Lenient: a profile with drifted JSON still lists (the import itself reports the error).
        IReadOnlyDictionary<string, string>? mapping = null;
        try
        {
            var spec = ParseMapping(profile.MappingJson);
            mapping = spec.Columns.ToDictionary(c => c.Key, c => ColumnLetter(c.Value));
        }
        catch (DomainValidationException)
        {
            // Keep mapping null; MappedFieldCount stays 0.
        }

        return new OrderImportProfileDto(
            profile.Id, profile.Name, profile.Description, profile.MappingJson, profile.IsActive,
            profile.CustomerId,
            profile.CustomerId is { } customerId ? customerNames.GetValueOrDefault(customerId) : null,
            mapping,
            ParseSourceHeaders(profile.SourceHeadersJson),
            mapping?.Count ?? 0,
            profile.UpdatedAt == default ? profile.CreatedAt : profile.UpdatedAt);
    }

    /// <summary>1 → "A", 28 → "AB" — the inverse of <see cref="TryParseColumnReference"/>.</summary>
    private static string ColumnLetter(int index)
    {
        var result = string.Empty;
        while (index > 0)
        {
            index -= 1;
            result = (char)('A' + (index % 26)) + result;
            index /= 26;
        }

        return result;
    }

    private static IReadOnlyList<string>? ParseSourceHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------- profile management (2026-09)

    public async Task<OrderImportProfileDto> CreateProfileAsync(
        SaveOrderImportProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = new OrderImportProfile { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        await ApplyProfileAsync(profile, request, cancellationToken);
        _dbContext.OrderImportProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderImportProfile", profile.Id.ToString(), "Created", null,
            new { profile.Name, profile.CustomerId, Fields = request.Mapping.Count }, cancellationToken);
        return (await ListProfilesAsync(cancellationToken, includeInactive: true)).First(p => p.Id == profile.Id);
    }

    public async Task<OrderImportProfileDto?> UpdateProfileAsync(
        Guid id, SaveOrderImportProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.OrderImportProfiles
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var before = new { profile.Name, profile.CustomerId, profile.MappingJson, profile.IsActive };
        await ApplyProfileAsync(profile, request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderImportProfile", profile.Id.ToString(), "Updated", before,
            new { profile.Name, profile.CustomerId, Fields = request.Mapping.Count, profile.IsActive }, cancellationToken);
        return (await ListProfilesAsync(cancellationToken, includeInactive: true)).First(p => p.Id == profile.Id);
    }

    public async Task<bool> DeleteProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.OrderImportProfiles
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (profile is null)
        {
            return false;
        }

        var inUse = await _dbContext.OrderImportBatches.AsNoTracking()
            .AnyAsync(b => b.TenantId == _tenantContext.TenantId && b.ProfileId == id, cancellationToken);
        if (inUse)
        {
            // History rows FK the profile (Restrict); deactivating preserves the import trail.
            throw new DomainValidationException(
                "Dit importprofiel werd al gebruikt voor imports. Zet het op inactief in plaats van het te verwijderen.");
        }

        _dbContext.Remove(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderImportProfile", profile.Id.ToString(), "Deleted",
            new { profile.Name }, null, cancellationToken);
        return true;
    }

    /// <summary>
    /// Validates and applies a save request. The mapping is serialized into the SAME MappingJson
    /// shape the importer reads, then round-tripped through <see cref="ParseMapping"/> so the
    /// editor and the importer can never disagree about what a valid profile is.
    /// </summary>
    private async Task ApplyProfileAsync(
        OrderImportProfile profile, SaveOrderImportProfileRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new DomainValidationException("name", "Geef het profiel een naam.");
        }

        if (name.Length > 100)
        {
            throw new DomainValidationException("name", "De naam mag maximaal 100 tekens lang zijn.");
        }

        var nameTaken = await _dbContext.OrderImportProfiles.AsNoTracking()
            .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.Id != profile.Id
                           && p.Name.ToLower() == name.ToLower(), cancellationToken);
        if (nameTaken)
        {
            throw new DomainValidationException("name", "Er bestaat al een importprofiel met deze naam.");
        }

        if (request.Description is { Length: > 500 })
        {
            throw new DomainValidationException("description", "De omschrijving mag maximaal 500 tekens lang zijn.");
        }

        if (request.CustomerId is { } customerId)
        {
            var customerExists = await _dbContext.Customers.AsNoTracking()
                .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);
            if (!customerExists)
            {
                throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
            }
        }

        if (request.HeaderRows is < 0 or > 10)
        {
            throw new DomainValidationException("headerRows", "Het aantal koprijen moet tussen 0 en 10 liggen.");
        }

        var columns = new Dictionary<string, string>();
        foreach (var (fieldKey, columnRef) in request.Mapping)
        {
            var field = KnownFields.FirstOrDefault(f => f.Equals(fieldKey, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                throw new DomainValidationException("mapping", $"Onbekend doelveld '{fieldKey}'.");
            }

            using var reference = JsonDocument.Parse(JsonSerializer.Serialize(columnRef));
            if (TryParseColumnReference(reference.RootElement) is not { } index)
            {
                throw new DomainValidationException("mapping", $"Ongeldige kolomverwijzing '{columnRef}' voor '{field}'.");
            }

            columns[field] = ColumnLetter(index);
        }

        var mappingJson = JsonSerializer.Serialize(new { headerRows = request.HeaderRows, columns });
        if (mappingJson.Length > 4000)
        {
            throw new DomainValidationException("mapping", "De mapping is te groot.");
        }

        // Single source of truth: the importer's own parser decides what a valid profile is
        // (incl. the mandatory unloading city/location column).
        ParseMapping(mappingJson);

        string? headersJson = null;
        if (request.SourceHeaders is { Count: > 0 })
        {
            headersJson = JsonSerializer.Serialize(
                request.SourceHeaders.Select(h => h.Length > 100 ? h[..100] : h).Take(100).ToList());
            if (headersJson.Length > 4000)
            {
                throw new DomainValidationException("sourceHeaders", "De kolomkoppen zijn samen te groot.");
            }
        }

        profile.Name = name;
        profile.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        profile.CustomerId = request.CustomerId;
        profile.MappingJson = mappingJson;
        profile.SourceHeadersJson = headersJson;
        profile.IsActive = request.IsActive;
    }

    // ------------------------------------------------------- sample-file analysis (2026-09)

    private const int MaxSampleValues = 3;

    public async Task<OrderImportAnalysisDto> AnalyzeAsync(byte[] fileBytes, CancellationToken cancellationToken)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(fileBytes));
        }
        catch
        {
            throw new DomainValidationException("Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new DomainValidationException("Het werkboek bevat geen werkblad.");

        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0)
        {
            throw new DomainValidationException("Het bestand bevat geen kolommen.");
        }

        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, 1 + MaxSampleValues);
        var columns = new List<OrderImportColumnAnalysisDto>();
        for (var column = 1; column <= lastColumn; column += 1)
        {
            var header = sheet.Cell(1, column).GetString().Trim();
            var samples = new List<string>();
            for (var row = 2; row <= lastRow; row += 1)
            {
                var value = sheet.Cell(row, column).GetString().Trim();
                if (value.Length > 0)
                {
                    samples.Add(value.Length > 60 ? value[..60] : value);
                }
            }

            var suggestion = header.Length > 0 ? OrderImportFields.Suggest(header) : null;
            columns.Add(new OrderImportColumnAnalysisDto(
                column, header, samples, suggestion?.Field, suggestion?.Confidence));
        }

        var fileHeaders = columns
            .Where(c => c.Header.Length > 0)
            .Select(c => OrderImportFields.NormalizeHeader(c.Header))
            .Where(h => h.Length > 0)
            .ToHashSet();
        var profileMatches = new List<OrderImportProfileMatchDto>();
        if (fileHeaders.Count > 0)
        {
            var profiles = await _dbContext.OrderImportProfiles.AsNoTracking()
                .Where(p => p.TenantId == _tenantContext.TenantId && p.IsActive && p.SourceHeadersJson != null)
                .ToListAsync(cancellationToken);
            foreach (var profile in profiles)
            {
                var stored = (ParseSourceHeaders(profile.SourceHeadersJson) ?? [])
                    .Select(OrderImportFields.NormalizeHeader)
                    .Where(h => h.Length > 0)
                    .ToHashSet();
                if (stored.Count == 0)
                {
                    continue;
                }

                var overlap = stored.Intersect(fileHeaders).Count();
                var percent = (int)Math.Round(overlap * 100.0 / Math.Max(stored.Count, fileHeaders.Count));
                if (percent >= 50)
                {
                    profileMatches.Add(new OrderImportProfileMatchDto(profile.Id, profile.Name, profile.CustomerId, percent));
                }
            }
        }

        return new OrderImportAnalysisDto(
            columns,
            profileMatches.OrderByDescending(m => m.MatchPercent).ThenBy(m => m.Name).Take(5).ToList());
    }

    /// <summary>
    /// ActivityTypeSeeder pattern: idempotent add-if-missing by Name, IgnoreQueryFilters so a
    /// deliberately deleted sample profile is never resurrected. The sample is plain
    /// configuration — no customer-specific logic lives in code.
    /// </summary>
    private async Task EnsureSampleProfileAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var exists = await _dbContext.OrderImportProfiles.IgnoreQueryFilters()
            .AnyAsync(p => p.TenantId == tenantId && p.Name == SampleProfileName, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.OrderImportProfiles.Add(new OrderImportProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = SampleProfileName,
            Description = "Voorbeeldprofiel: kolommen A t.e.m. O (referentie, datum, goederen, "
                + "hoeveelheid, eenheid, gewicht, laad- en losadres, ADR). Eerste rij = koptekst.",
            MappingJson = JsonSerializer.Serialize(new
            {
                headerRows = 1,
                columns = new Dictionary<string, string>
                {
                    ["customerReference"] = "A",
                    ["orderDate"] = "B",
                    ["goodsDescription"] = "C",
                    ["quantity"] = "D",
                    ["quantityUnit"] = "E",
                    ["weightKg"] = "F",
                    ["loadingLocation"] = "G",
                    ["loadingPostalCode"] = "H",
                    ["loadingCity"] = "I",
                    ["loadingCountry"] = "J",
                    ["unloadingLocation"] = "K",
                    ["unloadingPostalCode"] = "L",
                    ["unloadingCity"] = "M",
                    ["unloadingCountry"] = "N",
                    ["adr"] = "O",
                },
            }),
            IsActive = true,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------- batches

    public async Task<PagedResult<OrderImportBatchDto>> ListBatchesAsync(
        int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var query = _dbContext.OrderImportBatches.AsNoTracking()
            .Where(b => b.TenantId == _tenantContext.TenantId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Join(_dbContext.OrderImportProfiles.AsNoTracking().IgnoreQueryFilters()
                    .Where(p => p.TenantId == _tenantContext.TenantId),
                b => b.ProfileId, p => p.Id, (b, p) => new { b, ProfileName = p.Name })
            .Join(_dbContext.Customers.AsNoTracking().IgnoreQueryFilters()
                    .Where(c => c.TenantId == _tenantContext.TenantId),
                x => x.b.CustomerId, c => c.Id, (x, c) => new OrderImportBatchDto(
                    x.b.Id, x.b.ProfileId, x.ProfileName, x.b.CustomerId, c.Name, x.b.FileName,
                    x.b.Status, x.b.RowCount, x.b.SuccessCount, x.b.FailureCount, x.b.DryRun, x.b.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderImportBatchDto>(items, totalCount, pageRequest.Page, pageRequest.PageSize);
    }

    public async Task<OrderImportBatchDetailDto?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.OrderImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == _tenantContext.TenantId && b.Id == id, cancellationToken);
        if (batch is null)
        {
            return null;
        }

        return await BuildDetailAsync(batch, cancellationToken);
    }

    private async Task<OrderImportBatchDetailDto> BuildDetailAsync(
        OrderImportBatch batch, CancellationToken cancellationToken)
    {
        var profileName = await _dbContext.OrderImportProfiles.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenantContext.TenantId && p.Id == batch.ProfileId)
            .Select(p => p.Name).FirstOrDefaultAsync(cancellationToken) ?? "?";
        var customerName = await _dbContext.Customers.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == batch.CustomerId)
            .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "?";
        var rows = await _dbContext.OrderImportRows.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId && r.BatchId == batch.Id)
            .OrderBy(r => r.RowNumber)
            .Select(r => new OrderImportRowDto(r.RowNumber, r.Status, r.Error, r.CreatedTransportOrderId, r.ExternalReference))
            .ToListAsync(cancellationToken);

        return new OrderImportBatchDetailDto(
            new OrderImportBatchDto(
                batch.Id, batch.ProfileId, profileName, batch.CustomerId, customerName, batch.FileName,
                batch.Status, batch.RowCount, batch.SuccessCount, batch.FailureCount, batch.DryRun, batch.CreatedAt),
            rows);
    }

    // ------------------------------------------------------------- import

    public async Task<OrderImportBatchDetailDto> ImportAsync(
        Guid profileId, Guid customerId, string fileName, byte[] fileBytes, bool dryRun,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.OrderImportProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == profileId && p.IsActive,
                cancellationToken);
        if (profile is null)
        {
            throw new DomainValidationException("Het gekozen importprofiel bestaat niet of is inactief.");
        }

        var customerExists = await _dbContext.Customers.AsNoTracking()
            .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            throw new DomainValidationException("De gekozen klant bestaat niet.");
        }

        if (profile.CustomerId is { } boundCustomerId && boundCustomerId != customerId)
        {
            throw new DomainValidationException("Dit importprofiel is gekoppeld aan een andere klant.");
        }

        var mapping = ParseMapping(profile.MappingJson);

        var checksum = Convert.ToHexString(SHA256.HashData(fileBytes));
        // Duplicate detection: a file that already ran for REAL is refused. Dry runs never
        // block anything, and a batch that only Validated/Failed may always be retried.
        if (!dryRun)
        {
            var alreadyProcessed = await _dbContext.OrderImportBatches.AsNoTracking()
                .AnyAsync(b => b.TenantId == _tenantContext.TenantId && b.Sha256 == checksum
                               && !b.DryRun && b.Status == OrderImportBatchStatus.Processed, cancellationToken);
            if (alreadyProcessed)
            {
                throw new DomainValidationException("Dit bestand werd al verwerkt.");
            }
        }

        var parsedRows = ParseWorkbook(fileBytes, mapping);

        // Per-row external-reference dedupe set: non-cancelled orders of this customer.
        var existingReferences = (await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == _tenantContext.TenantId && o.CustomerId == customerId
                            && o.CustomerReference != null && o.Status != TransportOrderStatus.Cancelled)
                .Select(o => o.CustomerReference!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var batch = new OrderImportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            ProfileId = profile.Id,
            CustomerId = customerId,
            FileName = fileName.Length > 260 ? fileName[^260..] : fileName,
            Sha256 = checksum,
            DryRun = dryRun,
            Status = dryRun ? OrderImportBatchStatus.Validated : OrderImportBatchStatus.Processed,
        };

        var rows = new List<OrderImportRow>();
        foreach (var parsed in parsedRows)
        {
            var row = new OrderImportRow
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                BatchId = batch.Id,
                RowNumber = parsed.RowNumber,
                ExternalReference = parsed.CustomerReference,
            };
            rows.Add(row);

            if (parsed.Errors.Count > 0)
            {
                row.Status = OrderImportRowStatus.Error;
                row.Error = string.Join(" ", parsed.Errors);
                continue;
            }

            if (parsed.CustomerReference is not null && existingReferences.Contains(parsed.CustomerReference))
            {
                row.Status = OrderImportRowStatus.Skipped;
                row.Error = "Bestaat al (referentie).";
                continue;
            }

            if (dryRun)
            {
                // Would be created; nothing persists in a dry run.
                row.Status = OrderImportRowStatus.Created;
                if (parsed.CustomerReference is not null)
                {
                    existingReferences.Add(parsed.CustomerReference);
                }

                continue;
            }

            try
            {
                var result = await _orderService.CreateAsync(
                    parsed.Request! with { CustomerId = customerId }, cancellationToken);
                if (result.Outcome == TransportOrderOperationOutcome.Success)
                {
                    row.Status = OrderImportRowStatus.Created;
                    row.CreatedTransportOrderId = result.Order!.Id;
                    if (parsed.CustomerReference is not null)
                    {
                        existingReferences.Add(parsed.CustomerReference);
                    }
                }
                else
                {
                    row.Status = OrderImportRowStatus.Error;
                    row.Error = result.Error ?? $"Opdracht kon niet worden aangemaakt ({result.Outcome}).";
                    DetachStagedAdds();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                row.Status = OrderImportRowStatus.Error;
                row.Error = exception is DomainValidationException dve
                    ? dve.Message
                    : $"Onverwachte fout: {exception.Message}";
                DetachStagedAdds();
            }
        }

        batch.RowCount = rows.Count;
        batch.SuccessCount = rows.Count(r => r.Status == OrderImportRowStatus.Created);
        batch.FailureCount = rows.Count(r => r.Status == OrderImportRowStatus.Error);

        _dbContext.OrderImportBatches.Add(batch);
        _dbContext.OrderImportRows.AddRange(rows);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, batch.Id.ToString(),
            dryRun ? "Validated" : "Processed", null,
            new { batch.FileName, batch.RowCount, batch.SuccessCount, batch.FailureCount, batch.DryRun },
            cancellationToken);

        return await BuildDetailAsync(batch, cancellationToken);
    }

    /// <summary>
    /// Row isolation guard: a failed <c>CreateAsync</c> may leave half-staged (unsaved) adds in
    /// the shared change tracker; detach them so the NEXT row's save never flushes them. The
    /// batch/rows themselves are only added after all order creation, so they are never hit.
    /// </summary>
    private void DetachStagedAdds()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // ------------------------------------------------------------- mapping

    private sealed record MappingSpec(int HeaderRows, IReadOnlyDictionary<string, int> Columns);

    /// <summary>
    /// Lenient parse of the profile's mapping JSON: property names case-insensitive, column
    /// references as letters ("A", "AB") or 1-based indexes (number or numeric string).
    /// Requires at least an unloading city or unloading location column.
    /// </summary>
    private static MappingSpec ParseMapping(string mappingJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(mappingJson);
        }
        catch (JsonException)
        {
            throw new DomainValidationException("Het importprofiel bevat geen geldige JSON-mapping.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new DomainValidationException("Het importprofiel bevat geen geldige JSON-mapping.");
            }

            var headerRows = 1;
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals("headerRows", StringComparison.OrdinalIgnoreCase))
                {
                    headerRows = property.Value.ValueKind switch
                    {
                        JsonValueKind.Number when property.Value.TryGetInt32(out var n) => Math.Max(0, n),
                        JsonValueKind.String when int.TryParse(property.Value.GetString(), out var n) => Math.Max(0, n),
                        _ => 1,
                    };
                }
                else if (property.Name.Equals("columns", StringComparison.OrdinalIgnoreCase)
                         && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var column in property.Value.EnumerateObject())
                    {
                        var field = KnownFields.FirstOrDefault(f =>
                            f.Equals(column.Name, StringComparison.OrdinalIgnoreCase));
                        if (field is null)
                        {
                            continue; // unknown fields are ignored, never an error
                        }

                        if (TryParseColumnReference(column.Value) is { } index)
                        {
                            columns[field] = index;
                        }
                    }
                }
            }

            if (!columns.ContainsKey("unloadingCity") && !columns.ContainsKey("unloadingLocation"))
            {
                throw new DomainValidationException(
                    "Het importprofiel moet minstens een kolom voor de losplaats (gemeente) of loslocatie bevatten.");
            }

            return new MappingSpec(headerRows, columns);
        }
    }

    /// <summary>"A" → 1, "AB" → 28; a number or numeric string is taken as a 1-based index.</summary>
    private static int? TryParseColumnReference(JsonElement value)
    {
        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var n) => n.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (int.TryParse(text, out var index))
        {
            return index >= 1 ? index : null;
        }

        var result = 0;
        foreach (var character in text.ToUpperInvariant())
        {
            if (character is < 'A' or > 'Z')
            {
                return null;
            }

            result = (result * 26) + (character - 'A' + 1);
        }

        return result;
    }

    // ------------------------------------------------------------- workbook parsing

    private sealed record ParsedImportRow(
        int RowNumber, string? CustomerReference, CreateTransportOrderRequest? Request,
        IReadOnlyList<string> Errors);

    private List<ParsedImportRow> ParseWorkbook(byte[] fileBytes, MappingSpec mapping)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(fileBytes));
        }
        catch
        {
            throw new DomainValidationException("Het bestand is geen geldig Excel-werkboek (.xlsx).");
        }

        using var _ = workbook;
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new DomainValidationException("Het werkboek bevat geen werkblad.");

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var firstDataRow = mapping.HeaderRows + 1;
        if (lastRow - mapping.HeaderRows > MaxRows)
        {
            throw new DomainValidationException(
                $"Maximaal {MaxRows} rijen per import; dit bestand bevat er {lastRow - mapping.HeaderRows}.");
        }

        if (lastRow < firstDataRow)
        {
            throw new DomainValidationException("Het bestand bevat geen datarijen.");
        }

        var rows = new List<ParsedImportRow>();
        for (var rowNumber = firstDataRow; rowNumber <= lastRow; rowNumber += 1)
        {
            var row = sheet.Row(rowNumber);
            if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString())))
            {
                continue;
            }

            rows.Add(ParseRow(row, rowNumber, mapping));
        }

        if (rows.Count == 0)
        {
            throw new DomainValidationException("Het bestand bevat geen datarijen.");
        }

        return rows;
    }

    private ParsedImportRow ParseRow(IXLRow row, int rowNumber, MappingSpec mapping)
    {
        var errors = new List<string>();

        string? Text(string field) =>
            mapping.Columns.TryGetValue(field, out var column) ? NullIfEmpty(row.Cell(column).GetString().Trim()) : null;

        var customerReference = Text("customerReference");
        var goodsDescription = Text("goodsDescription");
        var quantityUnit = Text("quantityUnit");
        var orderDate = ParseDate(row, mapping, "orderDate", "Orderdatum", errors);
        var quantity = ParseDecimal(row, mapping, "quantity", "Hoeveelheid", errors);
        var weightKg = ParseDecimal(row, mapping, "weightKg", "Gewicht", errors);
        var adr = ParseBool(Text("adr"));

        var loadingLocation = Text("loadingLocation");
        var loadingPostalCode = Text("loadingPostalCode");
        var loadingCity = Text("loadingCity");
        var loadingCountry = NormalizeCountry(Text("loadingCountry"));
        var unloadingLocation = Text("unloadingLocation");
        var unloadingPostalCode = Text("unloadingPostalCode");
        var unloadingCity = Text("unloadingCity");
        var unloadingCountry = NormalizeCountry(Text("unloadingCountry"));

        // Lenient: a free-text location name doubles as the city when no city column/value
        // exists — the order service requires a city (or master location) on every stop.
        var effectiveUnloadingCity = unloadingCity ?? unloadingLocation;
        if (effectiveUnloadingCity is null)
        {
            errors.Add("Geef minstens de losplaats (gemeente) of loslocatie op.");
        }

        var hasLoading = loadingLocation is not null || loadingCity is not null
            || loadingPostalCode is not null || loadingCountry is not null;
        var effectiveLoadingCity = loadingCity ?? loadingLocation;
        if (hasLoading && effectiveLoadingCity is null)
        {
            errors.Add("Geef voor de laadstop een gemeente of locatienaam op.");
        }

        // Same minimum-cargo rule as the order service, reported per row in Dutch.
        var hasMeaningfulCargo = (quantity is > 0 && quantityUnit is not null) || goodsDescription is not null;
        if (!hasMeaningfulCargo)
        {
            errors.Add("Vul een goederenomschrijving of een hoeveelheid met eenheid in.");
        }

        if (errors.Count > 0)
        {
            return new ParsedImportRow(rowNumber, customerReference, null, errors);
        }

        var stops = new List<TransportOrderStopInput>();
        if (hasLoading)
        {
            stops.Add(new TransportOrderStopInput(
                StopType.Loading, null, loadingLocation, null, loadingPostalCode,
                effectiveLoadingCity, loadingCountry, null, null, null, null));
        }

        stops.Add(new TransportOrderStopInput(
            StopType.Unloading, null, unloadingLocation, null, unloadingPostalCode,
            effectiveUnloadingCity, unloadingCountry, null, null, null, null));

        var request = new CreateTransportOrderRequest(
            Guid.Empty, // customer id is stamped by the caller
            customerReference, orderDate, goodsDescription, quantity, quantityUnit,
            weightKg, null, null, adr, false, null, null, stops);

        return new ParsedImportRow(rowNumber, customerReference, request, errors);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NormalizeCountry(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is { Length: 2 } ? trimmed : null;
    }

    private static bool ParseBool(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "ja" or "j" or "x" or "true" or "1" or "yes" or "y";

    private static DateOnly? ParseDate(
        IXLRow row, MappingSpec mapping, string field, string label, List<string> errors)
    {
        if (!mapping.Columns.TryGetValue(field, out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.TryGetValue<DateTime>(out var dateTime) && dateTime != default)
        {
            return DateOnly.FromDateTime(dateTime);
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd.MM.yyyy"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text, CultureInfo.GetCultureInfo("nl-BE"), DateTimeStyles.None, out parsed))
        {
            return DateOnly.FromDateTime(parsed);
        }

        errors.Add($"{label} '{text}' is geen geldige datum.");
        return null;
    }

    private static decimal? ParseDecimal(
        IXLRow row, MappingSpec mapping, string field, string label, List<string> errors)
    {
        if (!mapping.Columns.TryGetValue(field, out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.TryGetValue<decimal>(out var numeric) && !cell.Value.IsText)
        {
            return numeric;
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add($"{label} '{text}' is geen geldig getal.");
        return null;
    }
}
