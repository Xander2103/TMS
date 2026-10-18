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
        await _countryValidator.NormalizeAndValidateAsync(request.CountryCode, "land (maatschappelijke zetel)", cancellationToken, "countryCode");
        await _countryValidator.NormalizeAndValidateAsync(request.OperationalCountryCode, "land (operationeel adres)", cancellationToken, "operationalCountryCode");

        var settings = await GetOrCreateAsync(cancellationToken);

        var before = AuditShape(settings);

        // Retry once/twice if a concurrent create claimed a numbering counter between our read
        // and save (the counters are concurrency tokens): reload and re-apply the request.
        for (var attempt = 0; ; attempt++)
        {
            // MD-H5: judged against the CURRENT counters on every attempt.
            await EnsureCountersSafeAsync(settings, request, cancellationToken);
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

        // MD-M10: every changed setting, numbering counters and prefixes included.
        if (Common.Integrity.AuditDiff.Changes(before, AuditShape(settings)) is { } diff)
        {
            await _auditService.RecordAsync(EntityType, settings.TenantId.ToString(), "Updated", diff.Old, diff.New, cancellationToken);
        }

        return Map(settings);
    }

    private static object AuditShape(TenantSettings s) => new
    {
        s.CompanyLegalName, s.TradingName, s.CompanyNumber, s.VatNumber,
        s.Street, s.HouseNumber, s.PostalCode, s.City, s.CountryCode,
        s.OperationalStreet, s.OperationalHouseNumber, s.OperationalPostalCode, s.OperationalCity, s.OperationalCountryCode,
        s.Email, s.PhoneNumber, s.Website, s.DefaultLanguage, s.Timezone, s.DefaultCurrency, s.DateFormat, s.DecimalSeparator,
        s.DefaultWeightUnit, s.DefaultDistanceUnit, s.InvoiceEmail, s.PaymentTermDays, s.DefaultVatRatePercent,
        s.DefaultLoadingMinutes, s.DefaultUnloadingMinutes, s.TrainingConflictSeverity, s.ShiftOverlapConflictSeverity,
        s.CapacityConflictSeverity, s.RedeliveryMode, s.EtaShiftNotifyMinutes, s.QualificationExpiryWarningDays,
        s.EmployeeNumberPrefix, s.EmployeeNumberNextValue, s.CustomerNumberPrefix, s.CustomerNumberNextValue,
        s.DriverNumberPrefix, s.DriverNumberNextValue, s.OrderNumberPrefix, s.OrderNumberNextValue,
        s.TripNumberPrefix, s.TripNumberNextValue, s.VehicleNumberPrefix, s.VehicleNumberNextValue,
        s.TrailerNumberPrefix, s.TrailerNumberNextValue, s.DefaultPageSize,
    };

    private const int CounterLookahead = 500;

    /// <summary>
    /// MD-H5 (master-data acceptance 2026-10-18): the order counter could be lowered from 104 to
    /// 100 ("✓ Opgeslagen") and the next order failed with a 500 on the unique index. A counter
    /// that is LOWERED, or whose prefix changes, may not point at numbers that already exist: the
    /// change is refused in Dutch, naming the highest number in use. Raising a counter is always
    /// safe. (A stale settings form that still carries an old, lower value is refused the same way.)
    /// </summary>
    private async Task EnsureCountersSafeAsync(TenantSettings current, UpdateCompanySettingsRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        async Task CheckAsync(string label, string? oldPrefix, int oldNext, string? newPrefixRaw, int newNextRaw, string defaultPrefix,
            string format, Func<IReadOnlyList<string>, Task<List<string>>> taken)
        {
            var newPrefix = Trim(newPrefixRaw) ?? defaultPrefix;
            var newNext = AtLeast(newNextRaw, 1);
            var prefixChanged = !string.Equals(newPrefix, oldPrefix ?? defaultPrefix, StringComparison.Ordinal);
            if (!prefixChanged && newNext >= oldNext)
            {
                return;
            }

            var upper = prefixChanged ? newNext + CounterLookahead : Math.Min(oldNext, newNext + CounterLookahead);
            var candidates = Enumerable.Range(newNext, Math.Max(upper - newNext, 1))
                .Select(v => newPrefix + v.ToString(format, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            var existing = await taken(candidates);
            if (existing.Count > 0)
            {
                var highest = existing.OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).Last();
                throw new Common.DomainValidationException(label,
                    $"Het volgnummer voor {LabelOf(label)} kan niet op {newPrefix}{newNext.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} gezet worden: "
                    + $"dat nummer of een volgend nummer is al in gebruik (hoogste in gebruik: {highest}). "
                    + "Kies een hoger volgnummer of een andere prefix. Herlaad de instellingen als iemand intussen nieuwe records aanmaakte.");
            }
        }

        await CheckAsync("orderNumberNextValue", current.OrderNumberPrefix, current.OrderNumberNextValue, request.OrderNumberPrefix,
            request.OrderNumberNextValue, string.Empty, "0000",
            c => _dbContext.TransportOrders.IgnoreQueryFilters().Where(o => o.TenantId == tenantId && c.Contains(o.OrderNumber)).Select(o => o.OrderNumber).ToListAsync(cancellationToken));
        await CheckAsync("customerNumberNextValue", current.CustomerNumberPrefix, current.CustomerNumberNextValue, request.CustomerNumberPrefix,
            request.CustomerNumberNextValue, string.Empty, "D4",
            c =>
            {
                var normalized = c.Select(Modules.Partners.Entities.Customer.NormalizeNumber).ToList();
                return _dbContext.Customers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && normalized.Contains(x.NormalizedCustomerNumber)).Select(x => x.CustomerNumber).ToListAsync(cancellationToken);
            });
        await CheckAsync("tripNumberNextValue", current.TripNumberPrefix, current.TripNumberNextValue, request.TripNumberPrefix,
            request.TripNumberNextValue, string.Empty, "0000",
            c => _dbContext.Trips.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && c.Contains(t.TripNumber)).Select(t => t.TripNumber).ToListAsync(cancellationToken));
        await CheckAsync("employeeNumberNextValue", current.EmployeeNumberPrefix, current.EmployeeNumberNextValue, request.EmployeeNumberPrefix,
            request.EmployeeNumberNextValue, string.Empty, "D4",
            c => _dbContext.Employees.IgnoreQueryFilters().Where(e => e.TenantId == tenantId && c.Contains(e.EmployeeNumber)).Select(e => e.EmployeeNumber).ToListAsync(cancellationToken));
        await CheckAsync("driverNumberNextValue", current.DriverNumberPrefix, current.DriverNumberNextValue, request.DriverNumberPrefix,
            request.DriverNumberNextValue, "CH-", "D4",
            c => _dbContext.Drivers.IgnoreQueryFilters().Where(d => d.TenantId == tenantId && c.Contains(d.DriverNumber)).Select(d => d.DriverNumber).ToListAsync(cancellationToken));
        await CheckAsync("vehicleNumberNextValue", current.VehicleNumberPrefix, current.VehicleNumberNextValue, request.VehicleNumberPrefix,
            request.VehicleNumberNextValue, "VRT-", "D4",
            c => _dbContext.Vehicles.IgnoreQueryFilters().Where(v => v.TenantId == tenantId && c.Contains(v.InternalNumber)).Select(v => v.InternalNumber).ToListAsync(cancellationToken));
        await CheckAsync("trailerNumberNextValue", current.TrailerNumberPrefix, current.TrailerNumberNextValue, request.TrailerNumberPrefix,
            request.TrailerNumberNextValue, "OPL-", "D4",
            c => _dbContext.Trailers.IgnoreQueryFilters().Where(t => t.TenantId == tenantId && c.Contains(t.InternalNumber)).Select(t => t.InternalNumber).ToListAsync(cancellationToken));
    }

    private static string LabelOf(string field) => field switch
    {
        "orderNumberNextValue" => "opdrachten",
        "customerNumberNextValue" => "klanten",
        "tripNumberNextValue" => "ritten",
        "employeeNumberNextValue" => "medewerkers",
        "driverNumberNextValue" => "chauffeurs",
        "vehicleNumberNextValue" => "voertuigen",
        _ => "opleggers",
    };

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

        // Gesloten productcatalogus (nl/fr/en) — onbekende waarden vallen terug op nl.
        settings.DefaultLanguage = Common.SupportedLanguages.Normalize(request.DefaultLanguage);
        settings.Timezone = Fallback(request.Timezone, "Europe/Amsterdam");
        settings.DefaultCurrency = Fallback(request.DefaultCurrency, "EUR").ToUpperInvariant();
        settings.DateFormat = NormalizeDateFormat(request.DateFormat, settings.DateFormat);
        settings.DecimalSeparator = request.DecimalSeparator is "," or "." ? request.DecimalSeparator : ",";
        settings.DefaultWeightUnit = Fallback(request.DefaultWeightUnit, "kg");
        settings.DefaultDistanceUnit = Fallback(request.DefaultDistanceUnit, "km");

        settings.Iban = Trim(request.Iban);
        settings.InvoiceEmail = Trim(request.InvoiceEmail);
        settings.PaymentTermDays = Clamp(request.PaymentTermDays, 0, 365);
        settings.DefaultVatRatePercent = request.DefaultVatRatePercent is >= 0 and <= 100 ? request.DefaultVatRatePercent : 21m;

        settings.DefaultLoadingMinutes = Clamp(request.DefaultLoadingMinutes, 0, 1440);
        settings.DefaultUnloadingMinutes = Clamp(request.DefaultUnloadingMinutes, 0, 1440);
        settings.TrainingConflictSeverity = NormalizeSeverity(request.TrainingConflictSeverity);
        settings.ShiftOverlapConflictSeverity = NormalizeSeverity(request.ShiftOverlapConflictSeverity);
        settings.CapacityConflictSeverity = NormalizeSeverity(request.CapacityConflictSeverity);
        settings.RedeliveryMode = request.RedeliveryMode is "Propose" or "Automatic" ? request.RedeliveryMode : "Manual";
        settings.EtaShiftNotifyMinutes = request.EtaShiftNotifyMinutes is >= 1 and <= 720
            ? request.EtaShiftNotifyMinutes
            : null;
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
        s.TrainingConflictSeverity, s.ShiftOverlapConflictSeverity, s.CapacityConflictSeverity,
        s.RedeliveryMode, s.EtaShiftNotifyMinutes,
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

    /// <summary>
    /// Regional-settings wave: the date format is a closed catalog, not free text — the whole
    /// frontend renders through ONE formatter driven by this value, so an arbitrary string
    /// would silently break every date on screen. "dd-MM-yyyy" stays supported for existing
    /// tenants (pre-wave default); new preference options are slash/ISO styles. Empty input
    /// keeps the current value; an unknown format is a validation error, never a silent reset.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedDateFormats =
        ["dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy"];

    private static string NormalizeDateFormat(string? requested, string current)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return string.IsNullOrWhiteSpace(current) ? "dd/MM/yyyy" : current;
        }

        var trimmed = requested.Trim();
        if (!SupportedDateFormats.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new Common.DomainValidationException("dateFormat",
                $"Onbekende datumnotatie. Toegestaan: {string.Join(", ", SupportedDateFormats)}.");
        }

        return trimmed;
    }
    private static string? Upper(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant()[..Math.Min(value.Trim().Length, maxLength)];
    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    private static int AtLeast(int value, int min) => value < min ? min : value;

    /// <summary>Stores conflict severities as canonical enum names; anything else becomes Warning.</summary>
    private static string NormalizeSeverity(string? value) =>
        Enum.TryParse<Common.Scheduling.ConflictSeverity>(value, true, out var parsed)
            ? parsed.ToString()
            : nameof(Common.Scheduling.ConflictSeverity.Warning);
}
