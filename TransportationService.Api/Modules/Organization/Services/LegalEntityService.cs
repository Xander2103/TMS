using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Dtos;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Organization.Services;

public class LegalEntityService : ILegalEntityService
{
    private const string EntityType = "LegalEntity";
    private const string LogoStorageCategory = "legal-entities";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;
    private readonly IFileStorageService _fileStorage;

    public LegalEntityService(TransportationDbContext dbContext, ITenantContext tenantContext,
        ICurrentUserContext currentUserContext, IAuditService auditService,
        ICountryCodeValidator countryValidator, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<LegalEntityDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        return await _dbContext.LegalEntities
            .Where(e => e.TenantId == _tenantContext.TenantId && (includeInactive || e.IsActive))
            .OrderByDescending(e => e.IsDefault).ThenBy(e => e.LegalName)
            .Select(e => Map(e))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LegalEntityOptionDto>> ListOptionsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.LegalEntities
            .Where(e => e.TenantId == _tenantContext.TenantId && e.IsActive)
            .OrderByDescending(e => e.IsDefault).ThenBy(e => e.LegalName)
            .Select(e => new LegalEntityOptionDto(e.Id, e.TradingName ?? e.LegalName, e.VatNumber, e.IsDefault, e.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<LegalEntityDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<LegalEntityDto> CreateAsync(SaveLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var entity = new LegalEntity { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        await ApplyAsync(entity, request, cancellationToken);

        var anyExisting = await _dbContext.LegalEntities
            .AnyAsync(e => e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (!anyExisting || request.IsDefault)
        {
            await ClearOtherDefaultsAsync(entity.Id, cancellationToken);
            entity.IsDefault = true;
        }

        _dbContext.LegalEntities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "Created", null,
            new { entity.LegalName, entity.VatNumber, entity.IsDefault }, cancellationToken);

        return Map(entity);
    }

    public async Task<LegalEntityDto?> UpdateAsync(Guid id, SaveLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var before = new { entity.LegalName, entity.VatNumber, entity.InvoiceNumberFormat, entity.InvoicePrefix, entity.IsDefault };

        await ApplyAsync(entity, request, cancellationToken);

        if (request.IsDefault && !entity.IsDefault)
        {
            await ClearOtherDefaultsAsync(entity.Id, cancellationToken);
            entity.IsDefault = true;
        }
        else if (!request.IsDefault && entity.IsDefault)
        {
            // Never allow a tenant without default: ignore unsetting unless another default exists.
            var otherDefault = await _dbContext.LegalEntities.AnyAsync(
                e => e.TenantId == _tenantContext.TenantId && e.Id != entity.Id && e.IsDefault, cancellationToken);
            if (otherDefault)
            {
                entity.IsDefault = false;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "Updated", before,
            new { entity.LegalName, entity.VatNumber, entity.InvoiceNumberFormat, entity.InvoicePrefix, entity.IsDefault }, cancellationToken);

        return Map(entity);
    }

    public async Task<LegalEntityDto?> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!isActive)
        {
            if (entity.IsDefault)
            {
                throw new DomainValidationException("De standaardentiteit kan niet worden gedeactiveerd. Stel eerst een andere standaardentiteit in.");
            }

            var otherActive = await _dbContext.LegalEntities.AnyAsync(
                e => e.TenantId == _tenantContext.TenantId && e.Id != entity.Id && e.IsActive, cancellationToken);
            if (!otherActive)
            {
                throw new DomainValidationException("Er moet minstens één actieve facturerende entiteit blijven.");
            }
        }

        if (entity.IsActive != isActive)
        {
            entity.IsActive = isActive;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, entity.Id.ToString(), isActive ? "Reactivated" : "Deactivated",
                new { IsActive = !isActive }, new { IsActive = isActive }, cancellationToken);
        }

        return Map(entity);
    }

    public async Task<LegalEntityDto?> AttachLogoAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var previousKey = entity.LogoStorageKey;
        entity.LogoStorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, LogoStorageCategory, fileName, content, cancellationToken);
        entity.LogoFileName = fileName;
        entity.LogoContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (previousKey is not null)
        {
            await _fileStorage.DeleteAsync(previousKey, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "LogoUploaded", null,
            new { FileName = fileName }, cancellationToken);

        return Map(entity);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenLogoAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity?.LogoStorageKey is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(entity.LogoStorageKey, cancellationToken);
        return (stream, entity.LogoFileName ?? "logo", entity.LogoContentType ?? "application/octet-stream");
    }

    public async Task<LegalEntityDto?> RemoveLogoAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (entity.LogoStorageKey is not null)
        {
            var key = entity.LogoStorageKey;
            entity.LogoStorageKey = null;
            entity.LogoFileName = null;
            entity.LogoContentType = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _fileStorage.DeleteAsync(key, cancellationToken);
            await _auditService.RecordAsync(EntityType, entity.Id.ToString(), "LogoRemoved", null, null, cancellationToken);
        }

        return Map(entity);
    }

    public async Task<ActiveLegalEntityDto> GetActiveSelectionAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserContext.CurrentUserId;
        if (userId is null)
        {
            return new ActiveLegalEntityDto(null);
        }

        var selection = await _dbContext.UserLegalEntitySelections
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId && s.UserId == userId, cancellationToken);

        if (selection is null)
        {
            return new ActiveLegalEntityDto(await GetDefaultEntityIdAsync(cancellationToken));
        }

        // Selection may point at a meanwhile-deactivated entity: fall back to the default.
        var stillActive = await _dbContext.LegalEntities.AnyAsync(
            e => e.TenantId == _tenantContext.TenantId && e.Id == selection.LegalEntityId && e.IsActive, cancellationToken);
        return new ActiveLegalEntityDto(stillActive ? selection.LegalEntityId : await GetDefaultEntityIdAsync(cancellationToken));
    }

    public async Task<ActiveLegalEntityDto> SetActiveSelectionAsync(Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = _currentUserContext.CurrentUserId
            ?? throw new DomainValidationException("Geen aangemelde gebruiker gevonden.");

        var selection = await _dbContext.UserLegalEntitySelections
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId && s.UserId == userId, cancellationToken);

        if (legalEntityId is null)
        {
            if (selection is not null)
            {
                _dbContext.UserLegalEntitySelections.Remove(selection);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new ActiveLegalEntityDto(await GetDefaultEntityIdAsync(cancellationToken));
        }

        var entity = await _dbContext.LegalEntities.FirstOrDefaultAsync(
            e => e.TenantId == _tenantContext.TenantId && e.Id == legalEntityId && e.IsActive, cancellationToken);
        if (entity is null)
        {
            throw new DomainValidationException("De gekozen facturerende entiteit bestaat niet of is niet actief.");
        }

        if (selection is null)
        {
            selection = new UserLegalEntitySelection
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                LegalEntityId = entity.Id,
            };
            _dbContext.UserLegalEntitySelections.Add(selection);
        }
        else
        {
            selection.LegalEntityId = entity.Id;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ActiveLegalEntityDto(entity.Id);
    }

    private async Task<Guid?> GetDefaultEntityIdAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.LegalEntities
            .Where(e => e.TenantId == _tenantContext.TenantId && e.IsActive)
            .OrderByDescending(e => e.IsDefault).ThenBy(e => e.LegalName)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<LegalEntity?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.LegalEntities
            .FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == id, cancellationToken);
    }

    private async Task ClearOtherDefaultsAsync(Guid exceptId, CancellationToken cancellationToken)
    {
        var others = await _dbContext.LegalEntities
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id != exceptId && e.IsDefault)
            .ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }

    private async Task ApplyAsync(LegalEntity entity, SaveLegalEntityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LegalName))
        {
            throw new DomainValidationException("legalName", "De juridische naam is verplicht.");
        }

        entity.LegalName = request.LegalName.Trim();
        entity.TradingName = Trim(request.TradingName);
        entity.CompanyNumber = Trim(request.CompanyNumber);
        entity.VatNumber = VatNumberValidator.NormalizeAndValidate(request.VatNumber);
        entity.PeppolId = Trim(request.PeppolId);
        entity.PeppolScheme = ValidatePeppolScheme(request.PeppolScheme);

        entity.Street = Trim(request.Street);
        entity.HouseNumber = Trim(request.HouseNumber);
        entity.PostalCode = Trim(request.PostalCode);
        entity.City = Trim(request.City);
        entity.CountryCode = await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land", cancellationToken, "countryCode");

        entity.Email = Trim(request.Email);
        entity.PhoneNumber = Trim(request.PhoneNumber);
        entity.Website = Trim(request.Website);

        entity.Iban = Trim(request.Iban)?.Replace(" ", string.Empty).ToUpperInvariant();
        entity.Bic = Trim(request.Bic)?.ToUpperInvariant();
        entity.BankName = Trim(request.BankName);

        var currency = Trim(request.DefaultCurrency)?.ToUpperInvariant() ?? "EUR";
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new DomainValidationException("defaultCurrency", "Een geldige valutacode van 3 letters is verplicht (bv. EUR).");
        }

        entity.DefaultCurrency = currency;
        entity.PaymentTermDays = Math.Clamp(request.PaymentTermDays, 0, 365);

        var format = Trim(request.InvoiceNumberFormat) ?? "{YYYY}{MM}{SEQ}";
        if (!format.Contains("{SEQ}", StringComparison.Ordinal))
        {
            throw new DomainValidationException("invoiceNumberFormat", "Het factuurnummerformaat moet de token {SEQ} bevatten.");
        }

        entity.InvoiceNumberFormat = format;
        entity.InvoiceSequencePadding = Math.Clamp(request.InvoiceSequencePadding, 2, 8);
        entity.InvoicePrefix = Trim(request.InvoicePrefix);
        entity.InvoiceFooter = Trim(request.InvoiceFooter);
    }

    private static string? ValidatePeppolScheme(string? scheme)
    {
        var trimmed = Trim(scheme);
        if (trimmed is null)
        {
            return null;
        }

        if (trimmed.Length != 4 || !trimmed.All(char.IsAsciiDigit))
        {
            throw new DomainValidationException("peppolScheme", "Het Peppol-schema moet uit 4 cijfers bestaan (bv. 0208).");
        }

        return trimmed;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LegalEntityDto Map(LegalEntity e) => new(
        e.Id, e.LegalName, e.TradingName, e.CompanyNumber, e.VatNumber, e.PeppolId, e.PeppolScheme,
        e.Street, e.HouseNumber, e.PostalCode, e.City, e.CountryCode,
        e.Email, e.PhoneNumber, e.Website,
        e.Iban, e.Bic, e.BankName,
        e.DefaultCurrency, e.PaymentTermDays,
        e.InvoiceNumberFormat, e.InvoiceSequencePadding, e.InvoicePrefix, e.InvoiceFooter,
        e.LogoStorageKey is not null, e.LogoFileName,
        e.IsActive, e.IsDefault);
}
