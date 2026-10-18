using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Dtos;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// MD-H5 (master-data acceptance 2026-10-18): parallel order / customer creates hit the unique
/// number index (PostgreSQL 23505) before the counter's concurrency token, and nothing retried it
/// (4 × 500 out of 5). A taken number — a parallel winner's or data ahead of the counter — is now
/// skipped forward; and an admin can no longer lower a counter onto numbers that exist. The true
/// parallel proof runs against PostgreSQL in the browser/API regression (20 parallel creates).
/// </summary>
public class NumberSequenceSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, DevTenantContext Tenant, AuditService Audit);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            CustomerNumberPrefix = "KL-", CustomerNumberNextValue = 1, DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-0001", Name = "Bestaand", IsActive = true });
        // Data AHEAD of the counters (SQL-cloned records, an import, a lowered counter).
        foreach (var number in new[] { "ORD-0001", "ORD-0002" })
        {
            db.Context.TransportOrders.Add(new TransportOrder
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 10, 1),
            });
        }

        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        return new Harness(db, tenantId, customerId, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
    }

    [Fact]
    public async Task OrderCreate_SkipsNumbersThatAreAlreadyTaken_InsteadOfA500()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orders = new TransportOrderService(h.Db.Context, h.Tenant, h.Audit, new TestClock(Now));

        var created = await orders.CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, null, new DateOnly(2026, 10, 18), "Kisten", null, null, null, null, null, false, false, null, null,
            [
                new TransportOrderStopInput(StopType.Loading, null, null, null, null, "Antwerpen", "BE", null, null, null, null),
                new TransportOrderStopInput(StopType.Unloading, null, null, null, null, "Gent", "BE", null, null, null, null),
            ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal("ORD-0003", created.Order!.OrderNumber);
    }

    [Fact]
    public async Task CustomerCreate_SkipsANumberThatIsAlreadyTaken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customers = new CustomerService(h.Db.Context, h.Tenant, h.Audit, new CountryCodeValidator(h.Db.Context));

        var created = await customers.CreateAsync(new CreateCustomerRequest(
            "Nieuw", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal("KL-0002", created.CustomerNumber);
    }

    [Fact]
    public async Task LoweringACounterOntoExistingNumbers_IsRefusedInDutch_RaisingIsFine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Db.Context.TenantSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderNumberNextValue, 3));
        var sut = new CompanySettingsService(h.Db.Context, h.Tenant, h.Audit, new CountryCodeValidator(h.Db.Context));

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateAsync(Request(orderNext: 1), CancellationToken.None));
        Assert.Contains("hoogste in gebruik: ORD-0002", refused.Message);
        Assert.True(refused.FieldErrors!.ContainsKey("orderNumberNextValue"));

        var raised = await sut.UpdateAsync(Request(orderNext: 50), CancellationToken.None);
        Assert.Equal(50, raised.OrderNumberNextValue);
        var audit = await h.Db.Context.AuditLogs.Where(a => a.EntityType == "CompanySettings").OrderBy(a => a.Timestamp).LastAsync();
        Assert.Contains("OrderNumberNextValue", audit.NewValuesJson);
    }

    private static UpdateCompanySettingsRequest Request(int orderNext) => new(
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
        EmployeeNumberPrefix: null, EmployeeNumberNextValue: 1,
        CustomerNumberPrefix: "KL-", CustomerNumberNextValue: 1,
        DriverNumberPrefix: null, DriverNumberNextValue: 1,
        OrderNumberPrefix: "ORD-", OrderNumberNextValue: orderNext,
        TripNumberPrefix: null, TripNumberNextValue: 1,
        InvoiceNumberPrefix: null, InvoiceNumberNextValue: 1,
        VehicleNumberPrefix: null, VehicleNumberNextValue: 1,
        TrailerNumberPrefix: null, TrailerNumberNextValue: 1,
        DefaultPageSize: 25, LogoReference: null);
}
