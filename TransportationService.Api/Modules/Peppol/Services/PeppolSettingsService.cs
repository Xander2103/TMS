using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Per-legal-entity Peppol settings, the tenant-wide configuration overview and the
/// "test own participant" sandbox check. Identifiers/VAT/IBAN are always read live from
/// <see cref="LegalEntity"/> — never duplicated onto <see cref="PeppolSettings"/>.
/// </summary>
public class PeppolSettingsService : IPeppolSettingsService
{
    private const string EntityType = "PeppolSettings";

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly IPeppolProviderFactory _providerFactory;

    public PeppolSettingsService(
        TransportationDbContext db, ITenantContext tenant, IAuditService audit, IPeppolProviderFactory providerFactory)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _providerFactory = providerFactory;
    }

    private Guid TenantId => _tenant.TenantId;

    public async Task<PeppolSettingsDto> GetSettingsAsync(Guid legalEntityId, CancellationToken cancellationToken)
    {
        var legalEntity = await GetOwnLegalEntityAsync(legalEntityId, cancellationToken);
        var settings = await FindSettingsAsync(legalEntityId, cancellationToken);
        return Map(legalEntity, settings);
    }

    public async Task<PeppolSettingsDto> SaveSettingsAsync(
        Guid legalEntityId, SavePeppolSettingsRequest request, CancellationToken cancellationToken)
    {
        var legalEntity = await GetOwnLegalEntityAsync(legalEntityId, cancellationToken);

        // IsDefined guards the string-stored column against numeric strings ("5") that
        // Enum.TryParse would otherwise accept as an undefined enum value.
        if (!Enum.TryParse<PeppolEnvironment>(request.Environment, ignoreCase: true, out var environment)
            || !Enum.IsDefined(environment))
        {
            throw new DomainValidationException("environment", "Kies een geldige omgeving (Sandbox of Live).");
        }

        var emailFallback = Trim(request.InvoiceEmailFallback);
        if (emailFallback is not null && (emailFallback.Length > 250 || !emailFallback.Contains('@')))
        {
            throw new DomainValidationException("invoiceEmailFallback",
                "Vul een geldig e-mailadres in voor de terugval (max. 250 tekens).");
        }

        // Fails fast with a clear Dutch message when the provider key is unknown.
        _providerFactory.Resolve(request.ProviderKey);

        var settings = await FindSettingsAsync(legalEntityId, cancellationToken);
        var isNew = settings is null;
        if (settings is null)
        {
            settings = new PeppolSettings { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = legalEntityId };
            _db.PeppolSettings.Add(settings);
        }

        var oldValues = isNew
            ? null
            : (object)new
            {
                settings.Enabled, Environment = settings.Environment.ToString(), settings.ProviderKey,
                settings.InvoiceEmailFallback, settings.DefaultInvoiceNote,
            };

        ApplyRequest(settings, request, environment, emailFallback);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (isNew)
        {
            // Concurrent first-time save: another request created the row between our lookup
            // and this insert (unique (TenantId, LegalEntityId) index). Re-read and update it.
            _db.Entry(settings).State = EntityState.Detached;
            settings = await FindSettingsAsync(legalEntityId, cancellationToken)
                ?? throw new DomainValidationException("Peppol-instellingen konden niet worden opgeslagen; probeer opnieuw.");
            isNew = false;
            ApplyRequest(settings, request, environment, emailFallback);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.RecordAsync(EntityType, settings.Id.ToString(), isNew ? "Created" : "Updated", oldValues,
            new
            {
                settings.Enabled, Environment = settings.Environment.ToString(), settings.ProviderKey,
                settings.InvoiceEmailFallback, settings.DefaultInvoiceNote,
            }, cancellationToken);

        return Map(legalEntity, settings);
    }

    public async Task<PeppolOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var legalEntities = await _db.LegalEntities
            .Where(e => e.TenantId == TenantId && e.IsActive)
            .OrderBy(e => e.LegalName)
            .ToListAsync(cancellationToken);
        var settingsByEntity = await _db.PeppolSettings
            .Where(s => s.TenantId == TenantId)
            .ToDictionaryAsync(s => s.LegalEntityId, cancellationToken);

        var checklist = legalEntities.Select(e =>
        {
            settingsByEntity.TryGetValue(e.Id, out var settings);
            var hasIdentity = !string.IsNullOrWhiteSpace(e.PeppolId) && !string.IsNullOrWhiteSpace(e.PeppolScheme);
            var hasVat = !string.IsNullOrWhiteSpace(e.VatNumber);
            var hasIban = !string.IsNullOrWhiteSpace(e.Iban);
            return new PeppolChecklistItemDto(
                e.Id, e.LegalName,
                hasIdentity, hasVat, hasIban,
                settings?.Enabled ?? false,
                (settings?.Environment ?? PeppolEnvironment.Sandbox).ToString(),
                settings?.ProviderKey ?? "sandbox",
                hasIdentity && hasVat && hasIban);
        }).ToList();

        var queued = await _db.PeppolTransmissions
            .CountAsync(t => t.TenantId == TenantId && t.Status == PeppolTransmissionStatus.Queued, cancellationToken);
        var delivered = await _db.PeppolTransmissions
            .CountAsync(t => t.TenantId == TenantId && t.Status == PeppolTransmissionStatus.Delivered, cancellationToken);
        var failed = await _db.PeppolTransmissions
            .CountAsync(t => t.TenantId == TenantId && t.Status == PeppolTransmissionStatus.Failed, cancellationToken);
        var incoming = await _db.PeppolIncomingDocuments.CountAsync(d => d.TenantId == TenantId, cancellationToken);

        var customersEnabledWithoutId = await _db.Customers.CountAsync(
            c => c.TenantId == TenantId && c.PeppolEnabled &&
                 (string.IsNullOrEmpty(c.PeppolId) || string.IsNullOrEmpty(c.PeppolScheme)), cancellationToken);
        var activeCustomersMissingData = await _db.Customers.CountAsync(
            c => c.TenantId == TenantId && c.IsActive &&
                 (string.IsNullOrEmpty(c.PeppolId) || string.IsNullOrEmpty(c.PeppolScheme)), cancellationToken);

        return new PeppolOverviewDto(
            checklist,
            new PeppolCountsDto(queued, delivered, failed, incoming),
            customersEnabledWithoutId,
            activeCustomersMissingData);
    }

    public async Task<PeppolTestConnectionResultDto> TestConnectionAsync(Guid legalEntityId, CancellationToken cancellationToken)
    {
        var legalEntity = await GetOwnLegalEntityAsync(legalEntityId, cancellationToken);
        if (string.IsNullOrWhiteSpace(legalEntity.PeppolId) || string.IsNullOrWhiteSpace(legalEntity.PeppolScheme))
        {
            throw new DomainValidationException(
                "Vul eerst een Peppol-ID en -schema in voor dit eigen bedrijf voordat je de verbinding test.");
        }

        var settings = await FindSettingsAsync(legalEntityId, cancellationToken);
        var provider = _providerFactory.Resolve(settings?.ProviderKey ?? "sandbox");
        var result = await provider.ValidateParticipantAsync(legalEntity.PeppolScheme, legalEntity.PeppolId, cancellationToken);

        // Audited against the legal entity: the check validates ITS participant identity and
        // must not depend on whether a settings row already exists.
        await _audit.RecordAsync("LegalEntity", legalEntityId.ToString(), "PeppolTestConnection",
            null, new { provider.Key, result.Found }, cancellationToken);

        return new PeppolTestConnectionResultDto(result.Found, result.SupportedDocumentTypes, result.ProviderReference,
            result.Found ? null : "Geen Peppol-deelnemer gevonden voor dit ID/schema bij de provider.");
    }

    private async Task<LegalEntity> GetOwnLegalEntityAsync(Guid legalEntityId, CancellationToken cancellationToken) =>
        await _db.LegalEntities.FirstOrDefaultAsync(e => e.TenantId == TenantId && e.Id == legalEntityId, cancellationToken)
        ?? throw new InvalidTenantReferenceException("eigen bedrijf");

    private Task<PeppolSettings?> FindSettingsAsync(Guid legalEntityId, CancellationToken cancellationToken) =>
        _db.PeppolSettings.FirstOrDefaultAsync(s => s.TenantId == TenantId && s.LegalEntityId == legalEntityId, cancellationToken);

    private static void ApplyRequest(
        PeppolSettings settings, SavePeppolSettingsRequest request, PeppolEnvironment environment, string? emailFallback)
    {
        settings.Enabled = request.Enabled;
        settings.Environment = environment;
        settings.ProviderKey = request.ProviderKey.Trim();
        settings.InvoiceEmailFallback = emailFallback;
        settings.DefaultInvoiceNote = Trim(request.DefaultInvoiceNote);
    }

    private static PeppolSettingsDto Map(LegalEntity legalEntity, PeppolSettings? settings) => new(
        legalEntity.Id, legalEntity.LegalName,
        settings?.Enabled ?? false,
        (settings?.Environment ?? PeppolEnvironment.Sandbox).ToString(),
        settings?.ProviderKey ?? "sandbox",
        settings?.InvoiceEmailFallback,
        settings?.DefaultInvoiceNote);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
