using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Reference.Services;

/// <summary>Full unit master record (Stamgegevens → Eenheden).</summary>
public record UnitTypeMasterDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive, int SortOrder,
    bool AllowForOrderEntry, bool AllowForPricing,
    UnitCategory Category, int Decimals, string? Symbol,
    UnitDimensionBehavior DimensionBehavior,
    decimal? DefaultLengthCm, decimal? DefaultWidthCm, decimal? DefaultHeightCm,
    decimal? DefaultWeightKg, decimal? MaxWeightKg, decimal? DefaultVolumeM3,
    decimal? DefaultLoadingMeters, decimal? DefaultPalletPlaces);

public record SaveUnitTypeMasterRequest(
    string Code, string Name, string? Description, bool IsActive, int SortOrder,
    bool AllowForOrderEntry, bool AllowForPricing,
    UnitCategory Category, int Decimals, string? Symbol,
    UnitDimensionBehavior DimensionBehavior,
    decimal? DefaultLengthCm, decimal? DefaultWidthCm, decimal? DefaultHeightCm,
    decimal? DefaultWeightKg, decimal? MaxWeightKg, decimal? DefaultVolumeM3,
    decimal? DefaultLoadingMeters, decimal? DefaultPalletPlaces);

public interface IUnitTypeMasterService
{
    Task<IReadOnlyList<UnitTypeMasterDto>> ListAsync(CancellationToken cancellationToken);
    Task<UnitTypeMasterDto> CreateAsync(SaveUnitTypeMasterRequest request, CancellationToken cancellationToken);
    Task<UnitTypeMasterDto?> UpdateAsync(Guid id, SaveUnitTypeMasterRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Master-data management of units incl. physical defaults. The code is user-editable master
/// data (legacy/accounting/EDI conventions); this service validates the format but NEVER
/// generates or regenerates a code — suggestions are a UI convenience only.
/// </summary>
public partial class UnitTypeMasterService : IUnitTypeMasterService
{
    [GeneratedRegex("^[A-Z0-9_-]{2,20}$")]
    private static partial Regex CodePattern();

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public UnitTypeMasterService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private Guid TenantId => _tenantContext.TenantId;

    public async Task<IReadOnlyList<UnitTypeMasterDto>> ListAsync(CancellationToken cancellationToken)
    {
        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .ToListAsync(cancellationToken);
        return units.Select(ToDto).ToList();
    }

    public async Task<UnitTypeMasterDto> CreateAsync(SaveUnitTypeMasterRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, existingId: null, cancellationToken);
        var unit = new UnitType { Id = Guid.NewGuid(), TenantId = TenantId };
        Apply(unit, request);
        _dbContext.UnitTypes.Add(unit);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("UnitType", unit.Id.ToString(), "Created", null,
            new { unit.Code, unit.Name, unit.Category, unit.DimensionBehavior }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(unit);
    }

    public async Task<UnitTypeMasterDto?> UpdateAsync(Guid id, SaveUnitTypeMasterRequest request, CancellationToken cancellationToken)
    {
        var unit = await _dbContext.UnitTypes
            .FirstOrDefaultAsync(u => u.TenantId == TenantId && u.Id == id, cancellationToken);
        if (unit is null)
        {
            return null;
        }

        await ValidateAsync(request, existingId: id, cancellationToken);
        var old = new { unit.Code, unit.Name, unit.Category, unit.DimensionBehavior };
        Apply(unit, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("UnitType", unit.Id.ToString(), "Updated", old,
            new { unit.Code, unit.Name, unit.Category, unit.DimensionBehavior }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(unit);
    }

    private async Task ValidateAsync(SaveUnitTypeMasterRequest request, Guid? existingId, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(code))
        {
            throw new DomainValidationException("Code moet 2-20 tekens zijn (A-Z, 0-9, - of _).");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("Naam is verplicht.");
        }

        if (request.Decimals is < 0 or > 4)
        {
            throw new DomainValidationException("Decimalen moet tussen 0 en 4 liggen.");
        }

        foreach (var value in new[]
                 {
                     request.DefaultLengthCm, request.DefaultWidthCm, request.DefaultHeightCm,
                     request.DefaultWeightKg, request.MaxWeightKg, request.DefaultVolumeM3,
                     request.DefaultLoadingMeters, request.DefaultPalletPlaces,
                 })
        {
            if (value is < 0)
            {
                throw new DomainValidationException("Fysieke standaardwaarden mogen niet negatief zijn.");
            }
        }

        var codeTaken = await _dbContext.UnitTypes
            .AnyAsync(u => u.TenantId == TenantId && u.Code == code && u.Id != existingId, cancellationToken);
        if (codeTaken)
        {
            throw new DomainValidationException($"De code {code} bestaat al voor een andere eenheid.");
        }
    }

    private static void Apply(UnitType unit, SaveUnitTypeMasterRequest request)
    {
        unit.Code = request.Code.Trim().ToUpperInvariant();
        unit.Name = request.Name.Trim();
        unit.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        unit.IsActive = request.IsActive;
        unit.SortOrder = request.SortOrder;
        unit.AllowForOrderEntry = request.AllowForOrderEntry;
        unit.AllowForPricing = request.AllowForPricing;
        unit.Category = request.Category;
        unit.Decimals = request.Decimals;
        unit.Symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim();
        unit.DimensionBehavior = request.DimensionBehavior;
        unit.DefaultLengthCm = request.DefaultLengthCm;
        unit.DefaultWidthCm = request.DefaultWidthCm;
        unit.DefaultHeightCm = request.DefaultHeightCm;
        unit.DefaultWeightKg = request.DefaultWeightKg;
        unit.MaxWeightKg = request.MaxWeightKg;
        unit.DefaultVolumeM3 = request.DefaultVolumeM3;
        unit.DefaultLoadingMeters = request.DefaultLoadingMeters;
        unit.DefaultPalletPlaces = request.DefaultPalletPlaces;
    }

    private static UnitTypeMasterDto ToDto(UnitType u) => new(
        u.Id, u.Code, u.Name, u.Description, u.IsActive, u.SortOrder,
        u.AllowForOrderEntry, u.AllowForPricing,
        u.Category, u.Decimals, u.Symbol, u.DimensionBehavior,
        u.DefaultLengthCm, u.DefaultWidthCm, u.DefaultHeightCm,
        u.DefaultWeightKg, u.MaxWeightKg, u.DefaultVolumeM3,
        u.DefaultLoadingMeters, u.DefaultPalletPlaces);
}
