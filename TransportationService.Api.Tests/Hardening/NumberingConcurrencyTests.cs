using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// The numbering counters on TenantSettings are optimistic-concurrency tokens. These tests
/// simulate the production race (a concurrent request advanced the counter after this
/// context read it) by bumping the DB row behind the tracked entity's back, and assert the
/// retry path recovers instead of issuing a duplicate number or failing.
/// </summary>
public class NumberingConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            VehicleNumberPrefix = "VRT-", VehicleNumberNextValue = 1,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId);
    }

    private static VehicleService BuildVehicleService(Harness h)
    {
        var tenant = new DevTenantContext(h.TenantId);
        return new VehicleService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)), TimeProvider.System);
    }

    private static CreateVehicleRequest VehicleRequest(string plate) => new(
        plate, null, null, null, null, null, null, FuelType.Diesel, null,
        null, null, null, null, null, null, 0, null, false, false, false, false,
        VehicleOwnershipType.Owned, null, null, null);

    [Fact]
    public async Task VehicleCreate_WithStaleCounter_RetriesAndClaimsFreshNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = BuildVehicleService(h);

        // First create claims VRT-0001 and leaves the tracked settings at NextValue=2.
        var first = await sut.CreateAsync(VehicleRequest("1-AAA-1"), CancellationToken.None);
        Assert.Equal("VRT-0001", first.Vehicle!.InternalNumber);

        // A "concurrent" request advances the counter directly in the database.
        await h.Db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE tenant_settings SET \"VehicleNumberNextValue\" = 7");

        // The tracked entity still believes NextValue=2 → the save conflicts on the
        // concurrency token, reloads, and must claim VRT-0007 (not the stale VRT-0002).
        var second = await sut.CreateAsync(VehicleRequest("1-AAA-2"), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.Success, second.Outcome);
        Assert.Equal("VRT-0007", second.Vehicle!.InternalNumber);

        var storedNext = await h.Db.Context.TenantSettings
            .Where(s => s.TenantId == h.TenantId)
            .Select(s => s.VehicleNumberNextValue)
            .FirstAsync();
        Assert.Equal(8, storedNext);
    }

    [Fact]
    public async Task CompanySettingsUpdate_WithStaleCounter_RetriesAndPersistsRequest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new CompanySettingsService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)), new CountryCodeValidator(h.Db.Context));

        // Load (and track) the settings, then bump a counter behind the tracker's back.
        await sut.GetAsync(CancellationToken.None);
        await h.Db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE tenant_settings SET \"EmployeeNumberNextValue\" = 99");

        var request = new TransportationService.Api.Modules.Tenancy.Dtos.UpdateCompanySettingsRequest(
            CompanyLegalName: "Acme BV", TradingName: null, CompanyNumber: null, VatNumber: null,
            Street: null, HouseNumber: null, PostalCode: null, City: null, CountryCode: null,
            OperationalStreet: null, OperationalHouseNumber: null, OperationalPostalCode: null,
            OperationalCity: null, OperationalCountryCode: null,
            Email: null, PhoneNumber: null, Website: null,
            DefaultLanguage: "nl", Timezone: "Europe/Brussels", DefaultCurrency: "EUR",
            DateFormat: "dd-MM-yyyy", DecimalSeparator: ",", DefaultWeightUnit: "kg", DefaultDistanceUnit: "km",
            Iban: null, InvoiceEmail: null, PaymentTermDays: 30, DefaultVatRatePercent: 21m,
            DefaultLoadingMinutes: 30, DefaultUnloadingMinutes: 30,
            TrainingConflictSeverity: "Warning", ShiftOverlapConflictSeverity: "Warning",
            QualificationExpiryWarningDays: 30,
            EmployeeNumberPrefix: "MED-", EmployeeNumberNextValue: 5,
            CustomerNumberPrefix: "KL-", CustomerNumberNextValue: 1,
            DriverNumberPrefix: "CH-", DriverNumberNextValue: 1,
            OrderNumberPrefix: "ORD-", OrderNumberNextValue: 1,
            TripNumberPrefix: "RIT-", TripNumberNextValue: 1,
            InvoiceNumberPrefix: "FAC-", InvoiceNumberNextValue: 1,
            VehicleNumberPrefix: "VRT-", VehicleNumberNextValue: 1,
            TrailerNumberPrefix: "OPL-", TrailerNumberNextValue: 1,
            DefaultPageSize: 25, LogoReference: null);

        // Without the retry this throws DbUpdateConcurrencyException; with it, the admin's
        // explicitly requested value (5) wins and persists.
        var result = await sut.UpdateAsync(request, CancellationToken.None);

        Assert.Equal("Acme BV", result.CompanyLegalName);
        Assert.Equal(5, result.EmployeeNumberNextValue);
    }
}
