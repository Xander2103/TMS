using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Dtos;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tenancy;

public class CompanySettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private static CompanySettingsService Build(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new CompanySettingsService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
    }

    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedTenantAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        return (db, tenantId);
    }

    private static UpdateCompanySettingsRequest ValidRequest() => new(
        CompanyLegalName: "Acme Transport BV", TradingName: "Acme", CompanyNumber: "0123.456.789", VatNumber: "BE0123456789",
        Street: "Havenlaan", HouseNumber: "1", PostalCode: "2000", City: "Antwerpen", CountryCode: "be",
        OperationalStreet: "Kaai", OperationalHouseNumber: "5", OperationalPostalCode: "2030", OperationalCity: "Antwerpen", OperationalCountryCode: "BE",
        Email: "info@acme.example", PhoneNumber: "+3230000000", Website: "https://acme.example",
        DefaultLanguage: "NL", Timezone: "Europe/Brussels", DefaultCurrency: "eur",
        DateFormat: "dd/MM/yyyy", DecimalSeparator: ",", DefaultWeightUnit: "kg", DefaultDistanceUnit: "km",
        Iban: "BE00", InvoiceEmail: "facturen@acme.example", PaymentTermDays: 30, DefaultVatRatePercent: 21m,
        DefaultLoadingMinutes: 45, DefaultUnloadingMinutes: 45, QualificationExpiryWarningDays: 60,
        EmployeeNumberPrefix: "MED-", EmployeeNumberNextValue: 5,
        CustomerNumberPrefix: "KL-", CustomerNumberNextValue: 5,
        DriverNumberPrefix: "CH-", DriverNumberNextValue: 5,
        OrderNumberPrefix: "ORD-", OrderNumberNextValue: 5,
        TripNumberPrefix: "RIT-", TripNumberNextValue: 5,
        InvoiceNumberPrefix: "FAC-", InvoiceNumberNextValue: 5,
        VehicleNumberPrefix: "VRT-", VehicleNumberNextValue: 5,
        TrailerNumberPrefix: "OPL-", TrailerNumberNextValue: 5,
        DefaultPageSize: 50, LogoReference: null);

    [Fact]
    public async Task Get_CreatesDefaultSettings_WhenNoneExist()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;

        var result = await Build(db, tenantId).GetAsync(CancellationToken.None);

        Assert.Equal("EUR", result.DefaultCurrency);
        Assert.Equal("dd-MM-yyyy", result.DateFormat);
        Assert.Equal(1, await db.Context.TenantSettings.CountAsync());
    }

    [Fact]
    public async Task Update_PersistsAndNormalizesValues()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);

        var result = await sut.UpdateAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal("Acme Transport BV", result.CompanyLegalName);
        Assert.Equal("BE", result.CountryCode);       // upper-cased
        Assert.Equal("EUR", result.DefaultCurrency);  // upper-cased
        Assert.Equal("nl", result.DefaultLanguage);   // lower-cased
        Assert.Equal(60, result.QualificationExpiryWarningDays);

        // Reload from a fresh service to confirm persistence.
        var reloaded = await Build(db, tenantId).GetAsync(CancellationToken.None);
        Assert.Equal("Acme Transport BV", reloaded.CompanyLegalName);
        Assert.Equal(50, reloaded.DefaultPageSize);
    }

    [Fact]
    public async Task Update_ClampsInvalidNumbersAndSeparators()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var sut = Build(db, tenantId);

        var request = ValidRequest() with
        {
            PaymentTermDays = -5,
            DefaultVatRatePercent = 250m,
            EmployeeNumberNextValue = 0,
            DecimalSeparator = "x",
            DefaultPageSize = 9999,
        };

        var result = await sut.UpdateAsync(request, CancellationToken.None);

        Assert.Equal(0, result.PaymentTermDays);
        Assert.Equal(21m, result.DefaultVatRatePercent);
        Assert.Equal(1, result.EmployeeNumberNextValue);
        Assert.Equal(",", result.DecimalSeparator);
        Assert.Equal(200, result.DefaultPageSize);
    }

    [Fact]
    public async Task Update_IsTenantIsolated()
    {
        var (db, tenantId) = await SeedTenantAsync();
        using var _ = db;
        var otherTenant = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        await Build(db, tenantId).UpdateAsync(ValidRequest() with { CompanyLegalName = "Tenant A" }, CancellationToken.None);
        await Build(db, otherTenant).UpdateAsync(ValidRequest() with { CompanyLegalName = "Tenant B" }, CancellationToken.None);

        Assert.Equal("Tenant A", (await Build(db, tenantId).GetAsync(CancellationToken.None)).CompanyLegalName);
        Assert.Equal("Tenant B", (await Build(db, otherTenant).GetAsync(CancellationToken.None)).CompanyLegalName);
    }
}
