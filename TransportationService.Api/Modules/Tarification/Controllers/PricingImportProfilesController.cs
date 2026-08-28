using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Controllers;

public record PricingImportProfileDto(
    Guid Id, string Name, string? Notes, int HeaderRow, string? SheetName,
    IReadOnlyDictionary<string, string> Mapping, bool IsActive);

public record SavePricingImportProfileRequest(
    string Name, string? Notes, int HeaderRow, string? SheetName,
    Dictionary<string, string>? Mapping, bool IsActive);

public record PricingImportRunDto(
    Guid Id, Guid AgreementId, Guid TargetAgreementId, string FileName, string Checksum,
    string? ProfileName, string Mode,
    int RowsRead, int RowsValid, int Created, int Updated, int Removed, int Failed,
    DateTime ImportedAt, Guid? ImportedByUserId,
    /// <summary>Succeeded / Rejected / Failed — see <see cref="PricingImportRunStatus"/>.</summary>
    string Status, string? Error);

/// <summary>
/// Sprint 4D/4F: reusable column-mapping profiles and the import history. Both are
/// configuration/traceability around the SAME pricing import — no separate Excel pricing engine.
/// </summary>
[ApiController]
public class PricingImportProfilesController : ControllerBase
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public PricingImportProfilesController(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private static PricingImportProfileDto Map(PricingImportProfile p) =>
        new(p.Id, p.Name, p.Notes, p.HeaderRow, p.SheetName,
            PricingImportColumns.ParseMapping(p.MappingJson), p.IsActive);

    /// <summary>The canonical fields a profile can map onto.</summary>
    [HttpGet("api/pricing/import/fields")]
    [RequirePermission(PermissionCodes.TariffsView)]
    public ActionResult<IReadOnlyList<PricingImportColumn>> Fields() => Ok(PricingImportColumns.All);

    [HttpGet("api/pricing/import/profiles")]
    [RequirePermission(PermissionCodes.TariffsView)]
    public async Task<ActionResult<IReadOnlyList<PricingImportProfileDto>>> List(CancellationToken cancellationToken)
    {
        var profiles = await _dbContext.PricingImportProfiles.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return Ok(profiles.Select(Map).ToList());
    }

    [HttpPost("api/pricing/import/profiles")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingImportProfileDto>> Create(
        SavePricingImportProfileRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("name", "Een naam voor het mappingprofiel is verplicht.");
        }

        if (await _dbContext.PricingImportProfiles
            .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.Name == name, cancellationToken))
        {
            throw new DomainValidationException("name", "Er bestaat al een mappingprofiel met deze naam.");
        }

        var profile = new PricingImportProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Name = name,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            HeaderRow = request.HeaderRow > 0 ? request.HeaderRow : 1,
            SheetName = string.IsNullOrWhiteSpace(request.SheetName) ? null : request.SheetName.Trim(),
            MappingJson = Serialize(request.Mapping),
            IsActive = request.IsActive,
        };
        _dbContext.PricingImportProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingImportProfile", profile.Id.ToString(), "Created", null,
            new { profile.Name, profile.HeaderRow, profile.SheetName }, cancellationToken);

        return Ok(Map(profile));
    }

    [HttpPut("api/pricing/import/profiles/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<PricingImportProfileDto>> Update(
        Guid id, SavePricingImportProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.PricingImportProfiles
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (profile is null) return NotFound();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("name", "Een naam voor het mappingprofiel is verplicht.");
        }

        // Same uniqueness rule as Create — otherwise the unique index turns a rename into a 500.
        if (await _dbContext.PricingImportProfiles
            .AnyAsync(p => p.TenantId == _tenantContext.TenantId && p.Id != id && p.Name == name, cancellationToken))
        {
            throw new DomainValidationException("name", "Er bestaat al een mappingprofiel met deze naam.");
        }

        var before = new { profile.Name, profile.HeaderRow, profile.SheetName, profile.MappingJson, profile.IsActive };
        profile.Name = name;
        profile.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        profile.HeaderRow = request.HeaderRow > 0 ? request.HeaderRow : 1;
        profile.SheetName = string.IsNullOrWhiteSpace(request.SheetName) ? null : request.SheetName.Trim();
        profile.MappingJson = Serialize(request.Mapping);
        profile.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("PricingImportProfile", profile.Id.ToString(), "Updated", before,
            new { profile.Name, profile.HeaderRow, profile.SheetName, profile.MappingJson, profile.IsActive },
            cancellationToken);
        return Ok(Map(profile));
    }

    [HttpDelete("api/pricing/import/profiles/{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.PricingImportProfiles
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Id == id, cancellationToken);
        if (profile is null) return NotFound();

        _dbContext.PricingImportProfiles.Remove(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingImportProfile", id.ToString(), "Deleted",
            new { profile.Name }, null, cancellationToken);
        return NoContent();
    }

    /// <summary>Import history, newest first; without an agreement id: the whole tenant.</summary>
    [HttpGet("api/pricing/import/history")]
    [RequirePermission(PermissionCodes.TariffsView)]
    public async Task<ActionResult<IReadOnlyList<PricingImportRunDto>>> History(
        [FromQuery] Guid? agreementId, CancellationToken cancellationToken)
    {
        var query = _dbContext.PricingImportRuns.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId);
        if (agreementId is { } id)
        {
            query = query.Where(r => r.AgreementId == id || r.TargetAgreementId == id);
        }

        var runs = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .Select(r => new PricingImportRunDto(
                r.Id, r.AgreementId, r.TargetAgreementId, r.FileName, r.Checksum, r.ProfileName, r.Mode,
                r.RowsRead, r.RowsValid, r.Created, r.Updated, r.Removed, r.Failed,
                r.CreatedAt, r.CreatedByUserId, r.Status, r.Error))
            .ToListAsync(cancellationToken);
        return Ok(runs);
    }

    private static string Serialize(Dictionary<string, string>? mapping)
    {
        if (mapping is null || mapping.Count == 0) return "{}";

        // Only known fields are stored, so a typo in the payload cannot silently create a
        // mapping entry that never matches anything.
        var known = PricingImportColumns.All.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var cleaned = mapping
            .Where(kv => known.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        return System.Text.Json.JsonSerializer.Serialize(cleaned);
    }
}
