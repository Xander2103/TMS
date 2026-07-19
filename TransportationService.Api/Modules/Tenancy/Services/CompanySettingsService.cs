using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Dtos;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy.Services;

public class CompanySettingsService : ICompanySettingsService
{
    private const string EntityType = "CompanySettings";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;

    public CompanySettingsService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICountryCodeValidator countryValidator)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _countryValidator = countryValidator;
    }

    public async Task<CompanySettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateAsync(cancellationToken);
        return Map(settings);
    }

    public async Task<CompanySettingsDto> UpdateAsync(UpdateCompanySettingsRequest request, CancellationToken cancellationToken)
    {
        await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land (maatschappelijke zetel)", cancellationToken);
        await _countryValidator.NormalizeAndValidateAsync(request.OperationalCountryCode, "land (operationeel adres)", cancellationToken);

        var settings = await GetOrCreateAsync(cancellationToken);

        var before = new { settings.CompanyLegalName, settings.VatNumber, settings.DefaultCurrency, settings.Timezone };

        // Retry once/twice if a concurrent create claimed a numbering counter between our read
        // and save (the counters are concurrency tokens): reload and re-apply the request.
        for (var attempt = 0; ; attempt++)
        {
            ApplyRequest(settings, request);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                await _dbContext.Entry(settings).ReloadAsync(cancellationToken);
            }
        }

        await _auditService.RecordAsync(EntityType, settings.TenantId.ToString(), "Updated", before,
            new { settings.CompanyLegalName, settings.VatNumber, settings.DefaultCurrency, settings.Timezone }, cancellationToken);

        return Map(settings);
    }

    private static void ApplyRequest(TenantSettings settings, UpdateCompanySettingsRequest request)
    {
        settings.CompanyLegalName = Trim(request.CompanyLegalName);
        settings.TradingName = Trim(request.TradingName);
        settings.CompanyNumber = Trim(request.CompanyNumber);
        settings.VatNumber = Trim(request.VatNumber);

        settings.Street = Trim(request.Street);
        settings.HouseNumber = Trim(request.HouseNumber);
        settings.PostalCode = Trim(request.PostalCode);
        settings.City = Trim(request.City);
        settings.CountryCode = Upper(request.CountryCode, 2);

        settings.OperationalStreet = Trim(request.OperationalStreet);
        settings.OperationalHouseNumber = Trim(request.OperationalHouseNumber);
        settings.OperationalPostalCode = Trim(request.OperationalPostalCode);
        settings.OperationalCity = Trim(request.OperationalCity);
        settings.OperationalCountryCode = Upper(request.OperationalCountryCode, 2);

        settings.Email = Trim(request.Email);
        settings.PhoneNumber = Trim(request.PhoneNumber);
        settings.Website = Trim(request.Website);

        settings.DefaultLanguage = Fallback(request.DefaultLanguage, "nl").ToLowerInvariant();
        settings.Timezone = Fallback(request.Timezone, "Europe/Amsterdam");
        settings.DefaultCurrency = Fallback(request.DefaultCurrency, "EUR").ToUpperInvariant();
        settings.DateFormat = Fallback(request.DateFormat, "dd-MM-yyyy");
        settings.DecimalSeparator = request.DecimalSeparator is "," or "." ? request.DecimalSeparator : ",";
        settings.DefaultWeightUnit = Fallback(request.DefaultWeightUnit, "kg");
        settings.DefaultDistanceUnit = Fallback(request.DefaultDistanceUnit, "km");

        settings.Iban = Trim(request.Iban);
        settings.InvoiceEmail = Trim(request.InvoiceEmail);
        settings.PaymentTermDays = Clamp(request.PaymentTermDays, 0, 365);
        settings.DefaultVatRatePercent = request.DefaultVatRatePercent is >= 0 and <= 100 ? request.DefaultVatRatePercent : 21m;

        settings.DefaultLoadingMinutes = Clamp(request.DefaultLoadingMinutes, 0, 1440);
        settings.DefaultUnloadingMinutes = Clamp(request.DefaultUnloadingMinutes, 0, 1440);
        settings.QualificationExpiryWarningDays = Clamp(request.QualificationExpiryWarningDays, 0, 365);

        settings.EmployeeNumberPrefix = Trim(request.EmployeeNumberPrefix);
        settings.EmployeeNumberNextValue = AtLeast(request.EmployeeNumberNextValue, 1);
        settings.CustomerNumberPrefix = Trim(request.CustomerNumberPrefix);
        settings.CustomerNumberNextValue = AtLeast(request.CustomerNumberNextValue, 1);
        settings.DriverNumberPrefix = Trim(request.DriverNumberPrefix);
        settings.DriverNumberNextValue = AtLeast(request.DriverNumberNextValue, 1);
        settings.OrderNumberPrefix = Trim(request.OrderNumberPrefix);
        settings.OrderNumberNextValue = AtLeast(request.OrderNumberNextValue, 1);
        settings.TripNumberPrefix = Trim(request.TripNumberPrefix);
        settings.TripNumberNextValue = AtLeast(request.TripNumberNextValue, 1);
        settings.InvoiceNumberPrefix = Trim(request.InvoiceNumberPrefix);
        settings.InvoiceNumberNextValue = AtLeast(request.InvoiceNumberNextValue, 1);
        settings.VehicleNumberPrefix = Trim(request.VehicleNumberPrefix);
        settings.VehicleNumberNextValue = AtLeast(request.VehicleNumberNextValue, 1);
        settings.TrailerNumberPrefix = Trim(request.TrailerNumberPrefix);
        settings.TrailerNumberNextValue = AtLeast(request.TrailerNumberNextValue, 1);

        settings.DefaultPageSize = Clamp(request.DefaultPageSize, 5, 200);
        settings.LogoReference = Trim(request.LogoReference);
    }

    private async Task<TenantSettings> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        if (settings is null)
        {
            settings = new TenantSettings { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
            _dbContext.TenantSettings.Add(settings);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent request created the row first (unique TenantId index): use theirs.
                _dbContext.Entry(settings).State = EntityState.Detached;
                settings = await _dbContext.TenantSettings
                    .FirstAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
            }
        }

        return settings;
    }

    private static CompanySettingsDto Map(TenantSettings s) => new(
        s.CompanyLegalName, s.TradingName, s.CompanyNumber, s.VatNumber,
        s.Street, s.HouseNumber, s.PostalCode, s.City, s.CountryCode,
        s.OperationalStreet, s.OperationalHouseNumber, s.OperationalPostalCode, s.OperationalCity, s.OperationalCountryCode,
        s.Email, s.PhoneNumber, s.Website,
        s.DefaultLanguage, s.Timezone, s.DefaultCurrency, s.DateFormat, s.DecimalSeparator, s.DefaultWeightUnit, s.DefaultDistanceUnit,
        s.Iban, s.InvoiceEmail, s.PaymentTermDays, s.DefaultVatRatePercent,
        s.DefaultLoadingMinutes, s.DefaultUnloadingMinutes,
        s.QualificationExpiryWarningDays,
        s.EmployeeNumberPrefix, s.EmployeeNumberNextValue,
        s.CustomerNumberPrefix, s.CustomerNumberNextValue,
        s.DriverNumberPrefix, s.DriverNumberNextValue,
        s.OrderNumberPrefix, s.OrderNumberNextValue,
        s.TripNumberPrefix, s.TripNumberNextValue,
        s.InvoiceNumberPrefix, s.InvoiceNumberNextValue,
        s.VehicleNumberPrefix, s.VehicleNumberNextValue,
        s.TrailerNumberPrefix, s.TrailerNumberNextValue,
        s.DefaultPageSize, s.LogoReference);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string? Upper(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant()[..Math.Min(value.Trim().Length, maxLength)];
    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    private static int AtLeast(int value, int min) => value < min ? min : value;
}
