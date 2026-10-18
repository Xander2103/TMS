using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Reference.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Modules.Warehousing.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// Master-data remediation 2026-10-18 — reference integrity (MD-B2, MD-H7, MD-H8, MD-H9, MD-M12):
/// an UNUSED master record may still be deleted, a REFERENCED one is refused with a Dutch
/// explanation listing what references it (409 via <see cref="ReferencedRecordException"/>), can
/// be deactivated, and its history stays intact. Business codes that history stores are never
/// handed out again (customer numbers, unit codes).
/// </summary>
public class MasterDataReferenceIntegrityTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private sealed record Seed(SqliteTestDbContext Db, Guid TenantId, DevTenantContext Tenant, AuditService Audit);

    private static async Task<Seed> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = Now });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TradingName = "Acme" });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        return new Seed(db, tenantId, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
    }

    private static CustomerService Customers(Seed s) => new(s.Db.Context, s.Tenant, s.Audit, new CountryCodeValidator(s.Db.Context));

    private static async Task<Guid> AddCustomerAsync(Seed s, string number)
    {
        var id = Guid.NewGuid();
        s.Db.Context.Customers.Add(new Customer { Id = id, TenantId = s.TenantId, CustomerNumber = number, Name = $"Klant {number}", IsActive = true });
        await s.Db.Context.SaveChangesAsync();
        return id;
    }

    private static CreateCustomerRequest NewCustomer(string name, string? number) => new(
        name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null, CustomerNumber: number);

    private static SaveUnitTypeMasterRequest UnitRequest(string code, string name) => new(
        code, name, null, true, 0, true, true, false, UnitCategory.Other, 0, null, UnitDimensionBehavior.Variable,
        null, null, null, null, null, null, null, null);

    private static TransportOrder Order(Seed s, Guid customerId, string number, string? unitCode = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerId = customerId, OrderNumber = number, OrderDate = new DateOnly(2026, 10, 17),
        Status = TransportOrderStatus.Completed, QuantityUnitCode = unitCode,
    };

    // ---- MD-B2: customer ------------------------------------------------------------------

    [Fact]
    public async Task ReferencedCustomer_DeleteIsRefused_WithTheCounts_AndStaysReadable()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var customerId = await AddCustomerAsync(s, "TMK-001");
        var order = Order(s, customerId, "ORD-0101");
        s.Db.Context.TransportOrders.Add(order);
        s.Db.Context.TransportDossiers.Add(new Modules.Dossiers.Entities.TransportDossier
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, DossierNumber = "DOS-0098", Title = "T", CustomerId = customerId,
        });
        s.Db.Context.Invoices.AddRange(
            new Invoice { Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerId = customerId, InvoiceNumber = "FAC-1", Status = InvoiceStatus.Sent },
            new Invoice { Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerId = customerId, InvoiceNumber = "CN-1", Kind = InvoiceKind.CreditNote, Status = InvoiceStatus.Sent });
        s.Db.Context.IssuedTransportDocuments.Add(new IssuedTransportDocument
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, TransportOrderId = order.Id, Kind = IssuedTransportDocumentKind.Cmr,
            DocumentNumber = "CMR-2026-00007", RequestId = Guid.NewGuid(), IssuedAt = Now,
        });
        await s.Db.Context.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => Customers(s).DeleteAsync(customerId, CancellationToken.None));

        Assert.StartsWith("Deze klant kan niet worden verwijderd", refused.Message);
        Assert.Contains("1 opdracht", refused.Message);
        Assert.Contains("1 dossier", refused.Message);
        Assert.Contains("1 factuur", refused.Message);
        Assert.Contains("1 creditnota", refused.Message);
        Assert.Contains("1 transportdocument", refused.Message);
        Assert.Contains("Deactiveren", refused.Message);
        Assert.True(refused.CanDeactivate);
        Assert.Equal(
            new[] { "orders", "dossiers", "invoices", "creditNotes", "transportDocuments" },
            refused.Report.Items.Select(i => i.Key));
        s.Db.Context.ChangeTracker.Clear();
        Assert.False((await s.Db.Context.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customerId)).IsDeleted);

        // Deactivating is the way out — and the customer stays readable for its history.
        Assert.True(await Customers(s).SetActiveAsync(customerId, new SetCustomerActiveRequest(false), CancellationToken.None));
        var detail = await Customers(s).GetByIdAsync(customerId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.False(detail!.IsActive);
    }

    [Fact]
    public async Task UnusedCustomer_CanStillBeDeleted_ButItsNumberIsNeverReused_CaseInsensitively()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var unused = await AddCustomerAsync(s, "TMK-009");
        // Contacts are the customer's own configuration: they do not block deleting an unused customer.
        s.Db.Context.CustomerContacts.Add(new CustomerContact { Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerId = unused, FirstName = "Els", LastName = "F" });
        await s.Db.Context.SaveChangesAsync();
        Assert.Empty((await Customers(s).GetReferencesAsync(unused, CancellationToken.None))!);

        Assert.True(await Customers(s).DeleteAsync(unused, CancellationToken.None));

        var reuse = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Customers(s).CreateAsync(NewCustomer("Nieuwe klant", "tmk-009"), CancellationToken.None));
        Assert.Contains("verwijderde klant", reuse.Message);

        await AddCustomerAsync(s, "TMK-001");
        var caseVariant = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Customers(s).CreateAsync(NewCustomer("Andere klant", " tmk-001 "), CancellationToken.None));
        Assert.Contains("al in gebruik", caseVariant.Message);
    }

    [Fact]
    public async Task CustomerNumber_UniqueIndex_IsCaseInsensitive_AndCoversDeletedRows()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var id = await AddCustomerAsync(s, "TMK-001");
        s.Db.Context.Customers.Remove(await s.Db.Context.Customers.SingleAsync(c => c.Id == id));
        await s.Db.Context.SaveChangesAsync();

        s.Db.Context.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerNumber = "tmk-001", Name = "Bypass", IsActive = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => s.Db.Context.SaveChangesAsync());
    }

    // ---- MD-H7: driver ---------------------------------------------------------------------

    private static async Task<(Guid DriverId, Guid VehicleId)> AddDriverAsync(Seed s, bool withTrip)
    {
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        s.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = s.TenantId, EmployeeNumber = "MED-9", FirstName = "Jan", LastName = "Janssens",
            IsActive = true, CreatedAt = Now, UpdatedAt = Now,
        });
        s.Db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = s.TenantId, DriverNumber = "CH-9", EmployeeId = employeeId, IsActive = true });
        s.Db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = s.TenantId, InternalNumber = "VRT-9", LicensePlate = "1-TMK-001", IsActive = true,
            FixedDriverId = driverId, CurrentDriverId = driverId,
        });
        if (withTrip)
        {
            s.Db.Context.Trips.Add(new Trip
            {
                Id = Guid.NewGuid(), TenantId = s.TenantId, TripNumber = "RIT-0063", TripDate = new DateOnly(2026, 10, 17),
                Status = TripStatus.Completed, DriverId = driverId, VehicleId = vehicleId,
            });
        }

        await s.Db.Context.SaveChangesAsync();
        return (driverId, vehicleId);
    }

    private static DriverService Drivers(Seed s) => new(s.Db.Context, s.Tenant, s.Audit, new QualificationStatusCalculator(), TimeProvider.System);

    [Fact]
    public async Task DriverWithCompletedTrips_IsNotDeleted_AndTheTripKeepsItsDriver()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var (driverId, _) = await AddDriverAsync(s, withTrip: true);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => Drivers(s).DeleteAsync(driverId, CancellationToken.None));
        Assert.Contains("1 rit", refused.Message);
        Assert.Contains("inactief", refused.Message);
        s.Db.Context.ChangeTracker.Clear();
        Assert.False((await s.Db.Context.Drivers.IgnoreQueryFilters().SingleAsync(d => d.Id == driverId)).IsDeleted);
        Assert.Equal(driverId, (await s.Db.Context.Trips.SingleAsync()).DriverId);
    }

    [Fact]
    public async Task UnusedDriver_IsDeleted_AndNoVehicleSlotKeepsPointingAtIt()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var (driverId, vehicleId) = await AddDriverAsync(s, withTrip: false);

        Assert.True(await Drivers(s).DeleteAsync(driverId, CancellationToken.None));

        s.Db.Context.ChangeTracker.Clear();
        var vehicle = await s.Db.Context.Vehicles.SingleAsync(v => v.Id == vehicleId);
        Assert.Null(vehicle.FixedDriverId);
        Assert.Null(vehicle.CurrentDriverId);
    }

    // ---- MD-H8: unit -----------------------------------------------------------------------

    private static LookupService<UnitType> UnitLookups(Seed s) =>
        new(s.Db.Context, s.Tenant, s.Audit, [new UnitTypeReferenceGuard(s.Db.Context, s.Tenant)]);

    [Fact]
    public async Task UnitInUse_CannotBeDeleted_NorRecodedNorRecreated()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var unitId = Guid.NewGuid();
        s.Db.Context.UnitTypes.Add(new UnitType { Id = unitId, TenantId = s.TenantId, Code = "TMKIST", Name = "TEST MASTER Kist", IsActive = true });
        var customerId = await AddCustomerAsync(s, "KL-1");
        s.Db.Context.TransportOrders.Add(Order(s, customerId, "ORD-0101", "TMKIST"));
        await s.Db.Context.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => UnitLookups(s).DeleteAsync(unitId, CancellationToken.None));
        Assert.StartsWith("Deze eenheid kan niet worden verwijderd", refused.Message);
        Assert.Contains("1 opdracht", refused.Message);

        var master = new UnitTypeMasterService(s.Db.Context, s.Tenant, s.Audit);
        var recode = await Assert.ThrowsAsync<DomainValidationException>(() => master.UpdateAsync(unitId,
            UnitRequest("TMKIST2", "TEST MASTER Kist"), CancellationToken.None));
        Assert.Contains("kan niet meer gewijzigd worden", recode.Message);

        // A rename keeps the code: allowed (and audited with only the changed field).
        await master.UpdateAsync(unitId, UnitRequest("TMKIST", "TEST MASTER Kist HERNOEMD"), CancellationToken.None);
        var audit = await s.Db.Context.AuditLogs.Where(a => a.EntityType == "UnitType" && a.Action == "Updated").SingleAsync();
        Assert.Contains("TEST MASTER Kist HERNOEMD", audit.NewValuesJson);
        Assert.DoesNotContain("SortOrder", audit.NewValuesJson);
    }

    [Fact]
    public async Task DeletedUnitCode_IsNeverHandedOutAgain()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var unitId = Guid.NewGuid();
        s.Db.Context.UnitTypes.Add(new UnitType { Id = unitId, TenantId = s.TenantId, Code = "TMKIST", Name = "Kist", IsActive = true });
        await s.Db.Context.SaveChangesAsync();
        Assert.True(await UnitLookups(s).DeleteAsync(unitId, CancellationToken.None)); // unused → deletable

        var master = new UnitTypeMasterService(s.Db.Context, s.Tenant, s.Audit);
        var recreate = await Assert.ThrowsAsync<DomainValidationException>(() =>
            master.CreateAsync(UnitRequest("TMKIST", "Vat ANDERE BETEKENIS"), CancellationToken.None));
        Assert.Contains("verwijderde eenheid", recreate.Message);
        var viaLookup = await UnitLookups(s).CreateAsync(new CreateLookupRequest("tmkist", "Vat", null, true, 0), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.DuplicateCode, viaLookup.Status);
    }

    [Fact]
    public async Task Order_RefusesUnknown_Inactive_AndNotForOrderEntry_Units_ButKeepsItsOwnHistoricalUnit()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        s.Db.Context.UnitTypes.AddRange(
            new UnitType { Id = Guid.NewGuid(), TenantId = s.TenantId, Code = "PALLET", Name = "Pallet", IsActive = true },
            new UnitType { Id = Guid.NewGuid(), TenantId = s.TenantId, Code = "OLD", Name = "Oud", IsActive = false },
            new UnitType { Id = Guid.NewGuid(), TenantId = s.TenantId, Code = "LITER", Name = "Liter", IsActive = true, AllowForOrderEntry = false });
        var customerId = await AddCustomerAsync(s, "KL-1");
        var orders = new TransportOrderService(s.Db.Context, s.Tenant, s.Audit, new TestClock(new DateTimeOffset(Now)));

        static CreateTransportOrderRequest Create(Guid customerId, string unit) => new(
            customerId, null, new DateOnly(2026, 10, 18), "Kisten", 4, null, null, null, null, false, false, null, null,
            [
                new TransportOrderStopInput(StopType.Loading, null, null, null, null, "Antwerpen", "BE", null, null, null, null),
                new TransportOrderStopInput(StopType.Unloading, null, null, null, null, "Gent", "BE", null, null, null, null),
            ],
            null, QuantityUnitCode: unit);

        foreach (var (code, fragment) in new[] { ("NIETBESTAAND", "Onbekende eenheid"), ("OLD", "inactief"), ("LITER", "niet toegelaten") })
        {
            var refused = await Assert.ThrowsAsync<DomainValidationException>(() => orders.CreateAsync(Create(customerId, code), CancellationToken.None));
            Assert.Contains(fragment, refused.Message);
        }

        var created = await orders.CreateAsync(Create(customerId, "pallet"), CancellationToken.None);
        Assert.NotNull(created.Order);
        Assert.Equal("PALLET", created.Order!.QuantityUnitCode);
    }

    // ---- MD-H9 / MD-M12 --------------------------------------------------------------------

    [Fact]
    public async Task ItemTemplateWithStockOrMovements_IsRefused_UnusedTemplateIsDeleted()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var used = Guid.NewGuid();
        var unused = Guid.NewGuid();
        s.Db.Context.IssuedItemTemplates.AddRange(
            new IssuedItemTemplate { Id = used, TenantId = s.TenantId, Name = "TEST HR Asset" },
            new IssuedItemTemplate { Id = unused, TenantId = s.TenantId, Name = "Ongebruikt" });
        s.Db.Context.IssuedItemVariants.Add(new IssuedItemVariant { Id = Guid.NewGuid(), TenantId = s.TenantId, TemplateId = used, Label = "M", CurrentStock = 4 });
        s.Db.Context.StockMovements.Add(new StockMovement { Id = Guid.NewGuid(), TenantId = s.TenantId, TemplateId = used, Quantity = 4, ResultingStock = 4 });
        s.Db.Context.IssuedItemVariants.Add(new IssuedItemVariant { Id = Guid.NewGuid(), TenantId = s.TenantId, TemplateId = unused, Label = "L" });
        await s.Db.Context.SaveChangesAsync();

        var report = await IssuedItemTemplateReferences.ForTemplateAsync(s.Db.Context, s.TenantId, used, CancellationToken.None);
        Assert.Equal(new[] { "stock", "movements" }, report.Items.Select(i => i.Key));
        Assert.False((await IssuedItemTemplateReferences.ForTemplateAsync(s.Db.Context, s.TenantId, unused, CancellationToken.None)).HasReferences);
    }

    [Fact]
    public async Task DockWithAppointments_IsRefused()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var warehouseId = Guid.NewGuid();
        var dockId = Guid.NewGuid();
        var freeDock = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        s.Db.Context.Locations.Add(new Modules.Locations.Entities.Location { Id = locationId, TenantId = s.TenantId, Name = "Depot", City = "Antwerpen", IsActive = true });
        s.Db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = s.TenantId, Name = "Magazijn", LocationId = locationId, IsActive = true });
        s.Db.Context.Docks.AddRange(
            new Dock { Id = dockId, TenantId = s.TenantId, WarehouseId = warehouseId, Code = "D2" },
            new Dock { Id = freeDock, TenantId = s.TenantId, WarehouseId = warehouseId, Code = "D3" });
        s.Db.Context.DockAppointments.Add(new DockAppointment
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, WarehouseId = warehouseId, DockId = dockId, PlannedStart = Now, PlannedEnd = Now.AddHours(1),
        });
        await s.Db.Context.SaveChangesAsync();
        var sut = new WarehouseAdminService(s.Db.Context, s.Tenant, s.Audit);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => sut.DeleteDockAsync(warehouseId, dockId, CancellationToken.None));
        Assert.Contains("1 dockafspraak", refused.Message);
        Assert.True(await sut.DeleteDockAsync(warehouseId, freeDock, CancellationToken.None));
    }

    [Fact]
    public async Task VehicleCategoryInUse_IsRefused()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var categoryId = Guid.NewGuid();
        s.Db.Context.VehicleCategories.Add(new VehicleCategory { Id = categoryId, TenantId = s.TenantId, Code = "TMPWAGE", Name = "TEST", IsActive = true });
        s.Db.Context.Vehicles.Add(new Vehicle { Id = Guid.NewGuid(), TenantId = s.TenantId, InternalNumber = "VRT-18", LicensePlate = "1-TMK-001", CategoryId = categoryId, IsActive = true });
        await s.Db.Context.SaveChangesAsync();
        var sut = new LookupService<VehicleCategory>(s.Db.Context, s.Tenant, s.Audit, [new VehicleCategoryReferenceGuard(s.Db.Context, s.Tenant)]);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => sut.DeleteAsync(categoryId, CancellationToken.None));
        Assert.Contains("1 voertuig", refused.Message);
    }
}
