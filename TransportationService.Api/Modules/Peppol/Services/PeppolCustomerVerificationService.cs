using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Backs <c>POST /api/customers/{id}/peppol/verify</c>: looks the customer's Peppol
/// participant identity up at the provider and stores the (sanitized, non-secret) outcome on
/// the customer record. Lives in the Peppol module — the provider seam belongs here — but
/// operates on the <see cref="Customer"/> entity owned by the Partners module, the same
/// cross-module DbContext access pattern used elsewhere (e.g. Accounting ↔ Invoicing).
/// </summary>
public class PeppolCustomerVerificationService : IPeppolCustomerVerificationService
{
    private const string EntityType = "Customer";

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly IPeppolProviderFactory _providerFactory;
    private readonly TimeProvider _timeProvider;

    public PeppolCustomerVerificationService(
        TransportationDbContext db, ITenantContext tenant, IAuditService audit,
        IPeppolProviderFactory providerFactory, TimeProvider timeProvider)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _providerFactory = providerFactory;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerPeppolVerifyResultDto?> VerifyAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.TenantId == _tenant.TenantId && c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(customer.PeppolId) || string.IsNullOrWhiteSpace(customer.PeppolScheme))
        {
            throw new DomainValidationException(
                "Vul eerst een Peppol-ID en -schema in voor deze klant voordat je de gegevens controleert.");
        }

        var providerKey = await ResolveProviderKeyAsync(customer.DefaultLegalEntityId, cancellationToken);
        var provider = _providerFactory.Resolve(providerKey);
        var result = await provider.ValidateParticipantAsync(customer.PeppolScheme, customer.PeppolId, cancellationToken);

        var oldValues = new { customer.PeppolValidationStatus, customer.PeppolValidatedAt, customer.PeppolValidationReference };
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        customer.PeppolValidationStatus = result.Found ? CustomerPeppolValidationStatus.Found : CustomerPeppolValidationStatus.NotFound;
        customer.PeppolValidatedAt = now;
        customer.PeppolValidationReference = result.ProviderReference;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(EntityType, customer.Id.ToString(), "PeppolVerified", oldValues,
            new { customer.PeppolValidationStatus, customer.PeppolValidatedAt, customer.PeppolValidationReference }, cancellationToken);

        return new CustomerPeppolVerifyResultDto(result.Found, result.SupportedDocumentTypes, now, result.ProviderReference);
    }

    private async Task<string> ResolveProviderKeyAsync(Guid? defaultLegalEntityId, CancellationToken cancellationToken)
    {
        if (defaultLegalEntityId is not { } legalEntityId)
        {
            return SandboxPeppolProvider.ProviderKey;
        }

        var providerKey = await _db.PeppolSettings
            .Where(s => s.TenantId == _tenant.TenantId && s.LegalEntityId == legalEntityId)
            .Select(s => s.ProviderKey)
            .FirstOrDefaultAsync(cancellationToken);
        return providerKey ?? SandboxPeppolProvider.ProviderKey;
    }
}
