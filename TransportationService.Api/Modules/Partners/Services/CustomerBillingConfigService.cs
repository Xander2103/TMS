using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Partners.Services;

public record CustomerDieselSurchargeDto(
    bool Enabled,
    decimal Percent,
    DieselSurchargeBasis Basis,
    DieselSurchargePresentation Presentation,
    DieselSurchargeRounding Rounding,
    string? FormulaDescription,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveUntil);

public record SaveCustomerDieselSurchargeRequest(
    bool Enabled,
    decimal Percent,
    DieselSurchargeBasis Basis,
    DieselSurchargePresentation Presentation,
    DieselSurchargeRounding Rounding,
    string? FormulaDescription,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveUntil);

public record CustomerPoNumberDto(Guid Id, string PoNumber, DateOnly ValidFrom, DateOnly? ValidUntil, string? Notes, bool IsEffectiveToday);

public record SaveCustomerPoNumberRequest(string PoNumber, DateOnly ValidFrom, DateOnly? ValidUntil, string? Notes);

public record SetPurchaseOrderPolicyRequest(PurchaseOrderPolicy Policy);

public record CustomerPoPolicyDto(PurchaseOrderPolicy Policy, string? EffectivePoNumber, IReadOnlyList<CustomerPoNumberDto> History);

public interface ICustomerBillingConfigService
{
    Task<CustomerDieselSurchargeDto?> GetSurchargeAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerDieselSurchargeDto?> SaveSurchargeAsync(Guid customerId, SaveCustomerDieselSurchargeRequest request, CancellationToken cancellationToken);

    Task<CustomerPoPolicyDto?> GetPoPolicyAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerPoPolicyDto?> SetPoPolicyAsync(Guid customerId, SetPurchaseOrderPolicyRequest request, CancellationToken cancellationToken);
    Task<CustomerPoPolicyDto?> AddPoNumberAsync(Guid customerId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken);
    Task<CustomerPoPolicyDto?> UpdatePoNumberAsync(Guid customerId, Guid poId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken);
    Task<bool> DeletePoNumberAsync(Guid customerId, Guid poId, CancellationToken cancellationToken);

    /// <summary>Effective PO number of the customer on a given date (latest ValidFrom wins).</summary>
    Task<string?> ResolveEffectivePoNumberAsync(Guid customerId, DateOnly date, CancellationToken cancellationToken);
}

public class CustomerBillingConfigService : ICustomerBillingConfigService
{
    private const string EntityType = "Customer";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public CustomerBillingConfigService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerDieselSurchargeDto?> GetSurchargeAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        var config = await FindSurchargeAsync(customerId, cancellationToken);
        return config is null
            ? new CustomerDieselSurchargeDto(false, 0m, DieselSurchargeBasis.OrderAmount,
                DieselSurchargePresentation.PerOrderLine, DieselSurchargeRounding.NearestCent, null, null, null)
            : Map(config);
    }

    public async Task<CustomerDieselSurchargeDto?> SaveSurchargeAsync(
        Guid customerId, SaveCustomerDieselSurchargeRequest request, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        if (request.Percent is < 0 or > 100)
        {
            throw new DomainValidationException("percent", "Het toeslagpercentage moet tussen 0 en 100 liggen.");
        }

        if (request.EffectiveFrom is { } from && request.EffectiveUntil is { } until && until < from)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
        }

        var config = await FindSurchargeAsync(customerId, cancellationToken);
        var before = config is null ? null : new { config.Enabled, config.Percent, config.Basis, config.Presentation };
        if (config is null)
        {
            config = new CustomerDieselSurcharge { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, CustomerId = customerId };
            _dbContext.CustomerDieselSurcharges.Add(config);
        }

        config.Enabled = request.Enabled;
        config.Percent = request.Percent;
        config.Basis = request.Basis;
        config.Presentation = request.Presentation;
        config.Rounding = request.Rounding;
        config.FormulaDescription = string.IsNullOrWhiteSpace(request.FormulaDescription) ? null : request.FormulaDescription.Trim();
        config.EffectiveFrom = request.EffectiveFrom;
        config.EffectiveUntil = request.EffectiveUntil;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "DieselSurchargeChanged", before,
            new { config.Enabled, config.Percent, config.Basis, config.Presentation, config.Rounding }, cancellationToken);

        return Map(config);
    }

    public async Task<CustomerPoPolicyDto?> GetPoPolicyAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(customerId, cancellationToken);
        return customer is null ? null : await BuildPoPolicyDtoAsync(customer, cancellationToken);
    }

    public async Task<CustomerPoPolicyDto?> SetPoPolicyAsync(
        Guid customerId, SetPurchaseOrderPolicyRequest request, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var before = new { Policy = customer.PurchaseOrderPolicy };
        customer.PurchaseOrderPolicy = request.Policy;
        // Legacy flag stays in sync (older order-intake validation reads it).
        customer.PurchaseOrderRequired = request.Policy == PurchaseOrderPolicy.Required;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "PoPolicyChanged", before,
            new { Policy = customer.PurchaseOrderPolicy }, cancellationToken);

        return await BuildPoPolicyDtoAsync(customer, cancellationToken);
    }

    public async Task<CustomerPoPolicyDto?> AddPoNumberAsync(
        Guid customerId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        ValidatePoNumber(request);

        _dbContext.CustomerPurchaseOrderNumbers.Add(new CustomerPurchaseOrderNumber
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
            PoNumber = request.PoNumber.Trim(),
            ValidFrom = request.ValidFrom,
            ValidUntil = request.ValidUntil,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "PoNumberAdded", null,
            new { request.PoNumber, request.ValidFrom, request.ValidUntil }, cancellationToken);

        return await BuildPoPolicyDtoAsync(customer, cancellationToken);
    }

    public async Task<CustomerPoPolicyDto?> UpdatePoNumberAsync(
        Guid customerId, Guid poId, SaveCustomerPoNumberRequest request, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(customerId, cancellationToken);
        var po = await _dbContext.CustomerPurchaseOrderNumbers.FirstOrDefaultAsync(
            p => p.TenantId == _tenantContext.TenantId && p.CustomerId == customerId && p.Id == poId, cancellationToken);
        if (customer is null || po is null)
        {
            return null;
        }

        ValidatePoNumber(request);

        var before = new { po.PoNumber, po.ValidFrom, po.ValidUntil };
        po.PoNumber = request.PoNumber.Trim();
        po.ValidFrom = request.ValidFrom;
        po.ValidUntil = request.ValidUntil;
        po.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, customerId.ToString(), "PoNumberChanged", before,
            new { po.PoNumber, po.ValidFrom, po.ValidUntil }, cancellationToken);

        return await BuildPoPolicyDtoAsync(customer, cancellationToken);
    }

    public async Task<bool> DeletePoNumberAsync(Guid customerId, Guid poId, CancellationToken cancellationToken)
    {
        var po = await _dbContext.CustomerPurchaseOrderNumbers.FirstOrDefaultAsync(
            p => p.TenantId == _tenantContext.TenantId && p.CustomerId == customerId && p.Id == poId, cancellationToken);
        if (po is null)
        {
            return false;
        }

        _dbContext.Remove(po); // soft delete: history is preserved
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, customerId.ToString(), "PoNumberRemoved",
            new { po.PoNumber, po.ValidFrom }, null, cancellationToken);
        return true;
    }

    public async Task<string?> ResolveEffectivePoNumberAsync(Guid customerId, DateOnly date, CancellationToken cancellationToken)
    {
        return await _dbContext.CustomerPurchaseOrderNumbers
            .Where(p => p.TenantId == _tenantContext.TenantId && p.CustomerId == customerId
                        && p.ValidFrom <= date && (p.ValidUntil == null || p.ValidUntil >= date))
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => p.PoNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ValidatePoNumber(SaveCustomerPoNumberRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PoNumber))
        {
            throw new DomainValidationException("poNumber", "Het PO-nummer is verplicht.");
        }

        if (request.ValidUntil is { } until && until < request.ValidFrom)
        {
            throw new DomainValidationException("validUntil", "De einddatum ligt vóór de begindatum.");
        }
    }

    private async Task<CustomerPoPolicyDto> BuildPoPolicyDtoAsync(Customer customer, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var history = await _dbContext.CustomerPurchaseOrderNumbers
            .Where(p => p.TenantId == _tenantContext.TenantId && p.CustomerId == customer.Id)
            .OrderByDescending(p => p.ValidFrom)
            .ToListAsync(cancellationToken);
        var effective = history
            .Where(p => p.ValidFrom <= today && (p.ValidUntil is null || p.ValidUntil >= today))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefault();

        return new CustomerPoPolicyDto(
            customer.PurchaseOrderPolicy,
            effective?.PoNumber,
            history.Select(p => new CustomerPoNumberDto(
                p.Id, p.PoNumber, p.ValidFrom, p.ValidUntil, p.Notes, ReferenceEquals(p, effective))).ToList());
    }

    private static CustomerDieselSurchargeDto Map(CustomerDieselSurcharge s) => new(
        s.Enabled, s.Percent, s.Basis, s.Presentation, s.Rounding,
        s.FormulaDescription, s.EffectiveFrom, s.EffectiveUntil);

    private Task<CustomerDieselSurcharge?> FindSurchargeAsync(Guid customerId, CancellationToken cancellationToken) =>
        _dbContext.CustomerDieselSurcharges.FirstOrDefaultAsync(
            s => s.TenantId == _tenantContext.TenantId && s.CustomerId == customerId, cancellationToken);

    private Task<Customer?> FindCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        _dbContext.Customers.FirstOrDefaultAsync(
            c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken)!;

    private Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken) =>
        _dbContext.Customers.AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);
}
