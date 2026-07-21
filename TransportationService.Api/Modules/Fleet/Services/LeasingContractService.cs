using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public record LeasingContractDto(
    Guid Id, Guid? VehicleId, Guid? TrailerId, string LeasingCompany, string? ContractNumber,
    DateOnly? StartDate, DateOnly? EndDate,
    // Financial fields: null when the caller lacks fleet_finance.view.
    decimal? MonthlyAmount, string Currency, int? KilometerAllowancePerYear, int? EndOfContractMileageKm,
    string? ContactPerson, string? Notes, bool HasAttachment, string? FileName, bool IsActive);

public record SaveLeasingContractRequest(
    string LeasingCompany, string? ContractNumber, DateOnly? StartDate, DateOnly? EndDate,
    decimal? MonthlyAmount, string? Currency, int? KilometerAllowancePerYear, int? EndOfContractMileageKm,
    string? ContactPerson, string? Notes, bool IsActive);

public interface ILeasingContractService
{
    Task<IReadOnlyList<LeasingContractDto>?> ListForVehicleAsync(Guid vehicleId, bool includeFinance, CancellationToken cancellationToken);
    Task<IReadOnlyList<LeasingContractDto>?> ListForTrailerAsync(Guid trailerId, bool includeFinance, CancellationToken cancellationToken);
    Task<LeasingContractDto> CreateForVehicleAsync(Guid vehicleId, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken);
    Task<LeasingContractDto> CreateForTrailerAsync(Guid trailerId, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken);
    Task<LeasingContractDto?> UpdateAsync(Guid id, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<LeasingContractDto?> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, bool includeFinance, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken);
}

public class LeasingContractService : ILeasingContractService
{
    private const string EntityType = "LeasingContract";
    private const string StorageCategory = "leasing-contracts";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;

    public LeasingContractService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<LeasingContractDto>?> ListForVehicleAsync(Guid vehicleId, bool includeFinance, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Vehicles.AnyAsync(v => v.TenantId == _tenantContext.TenantId && v.Id == vehicleId, cancellationToken))
        {
            return null;
        }

        return await ListAsync(c => c.VehicleId == vehicleId, includeFinance, cancellationToken);
    }

    public async Task<IReadOnlyList<LeasingContractDto>?> ListForTrailerAsync(Guid trailerId, bool includeFinance, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Trailers.AnyAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == trailerId, cancellationToken))
        {
            return null;
        }

        return await ListAsync(c => c.TrailerId == trailerId, includeFinance, cancellationToken);
    }

    public Task<LeasingContractDto> CreateForVehicleAsync(Guid vehicleId, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken) =>
        CreateAsync(vehicleId, null, request, includeFinance, cancellationToken);

    public Task<LeasingContractDto> CreateForTrailerAsync(Guid trailerId, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken) =>
        CreateAsync(null, trailerId, request, includeFinance, cancellationToken);

    private async Task<LeasingContractDto> CreateAsync(Guid? vehicleId, Guid? trailerId, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken)
    {
        Validate(request);
        var contract = new LeasingContract
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            TrailerId = trailerId,
        };
        Apply(contract, request);
        _dbContext.LeasingContracts.Add(contract);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, contract.Id.ToString(), "Created", null,
            new { contract.LeasingCompany, contract.ContractNumber, contract.EndDate }, cancellationToken);
        return Map(contract, includeFinance);
    }

    public async Task<LeasingContractDto?> UpdateAsync(Guid id, SaveLeasingContractRequest request, bool includeFinance, CancellationToken cancellationToken)
    {
        var contract = await FindAsync(id, cancellationToken);
        if (contract is null)
        {
            return null;
        }

        Validate(request);
        Apply(contract, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, contract.Id.ToString(), "Updated", null,
            new { contract.LeasingCompany, contract.EndDate, contract.IsActive }, cancellationToken);
        return Map(contract, includeFinance);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await FindAsync(id, cancellationToken);
        if (contract is null)
        {
            return false;
        }

        _dbContext.Remove(contract);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, contract.Id.ToString(), "Deleted", new { contract.LeasingCompany }, null, cancellationToken);
        return true;
    }

    public async Task<LeasingContractDto?> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, bool includeFinance, CancellationToken cancellationToken)
    {
        var contract = await FindAsync(id, cancellationToken);
        if (contract is null)
        {
            return null;
        }

        var previous = contract.StorageKey;
        contract.StorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        contract.FileName = fileName;
        contract.ContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (previous is not null)
        {
            await _fileStorage.DeleteAsync(previous, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, contract.Id.ToString(), "DocumentUploaded", null, new { FileName = fileName }, cancellationToken);
        return Map(contract, includeFinance);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var contract = await FindAsync(id, cancellationToken);
        if (contract?.StorageKey is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(contract.StorageKey, cancellationToken);
        return (stream, contract.FileName ?? "leasing", contract.ContentType ?? "application/octet-stream");
    }

    private async Task<IReadOnlyList<LeasingContractDto>> ListAsync(
        System.Linq.Expressions.Expression<Func<LeasingContract, bool>> filter, bool includeFinance, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.LeasingContracts.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId)
            .Where(filter)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync(cancellationToken);
        return rows.Select(c => Map(c, includeFinance)).ToList();
    }

    private static void Validate(SaveLeasingContractRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LeasingCompany))
        {
            throw new DomainValidationException("leasingCompany", "De leasingmaatschappij is verplicht.");
        }

        if (request.StartDate is { } start && request.EndDate is { } end && end < start)
        {
            throw new DomainValidationException("endDate", "De einddatum kan niet vóór de begindatum liggen.");
        }
    }

    private static void Apply(LeasingContract c, SaveLeasingContractRequest r)
    {
        c.LeasingCompany = r.LeasingCompany.Trim();
        c.ContractNumber = Trim(r.ContractNumber);
        c.StartDate = r.StartDate;
        c.EndDate = r.EndDate;
        c.MonthlyAmount = r.MonthlyAmount is < 0 ? null : r.MonthlyAmount;
        c.Currency = string.IsNullOrWhiteSpace(r.Currency) ? "EUR" : r.Currency.Trim().ToUpperInvariant();
        c.KilometerAllowancePerYear = r.KilometerAllowancePerYear is < 0 ? null : r.KilometerAllowancePerYear;
        c.EndOfContractMileageKm = r.EndOfContractMileageKm is < 0 ? null : r.EndOfContractMileageKm;
        c.ContactPerson = Trim(r.ContactPerson);
        c.Notes = Trim(r.Notes);
        c.IsActive = r.IsActive;
    }

    private Task<LeasingContract?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.LeasingContracts.FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == id, cancellationToken)!;

    private static LeasingContractDto Map(LeasingContract c, bool includeFinance) => new(
        c.Id, c.VehicleId, c.TrailerId, c.LeasingCompany, c.ContractNumber, c.StartDate, c.EndDate,
        includeFinance ? c.MonthlyAmount : null, c.Currency,
        includeFinance ? c.KilometerAllowancePerYear : null,
        includeFinance ? c.EndOfContractMileageKm : null,
        c.ContactPerson, c.Notes, c.StorageKey is not null, c.FileName, c.IsActive);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
