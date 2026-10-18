using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// MD-B1 (master-data acceptance 2026-10-18, BLOCKER): an issued CMR / delivery note was re-rendered
/// from LIVE master data, so renaming the customer, the legal entity, re-plating the vehicle or
/// renaming / deleting-and-recreating a unit changed a legal document after the fact. An issued
/// document now renders from the snapshot frozen at issue — byte-identical on every download — and
/// only a NEW document picks up the new master data.
/// </summary>
public class IssuedTransportDocumentImmutabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid OrderId, Guid CustomerId, Guid LegalEntityId, Guid VehicleId, Guid UnitId, Guid EmployeeId)
    {
        public IssuedTransportDocumentService Sut()
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(Guid.NewGuid());
            return new IssuedTransportDocumentService(
                Db.Context, tenant, new AuditService(Db.Context, tenant, user), new TransportDocumentService(Db.Context, tenant),
                new TestClock(Now), user);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = entityId, TenantId = tenantId, LegalName = "Acme Transport BV", VatNumber = "BE0123456749",
            Street = "Transportlaan", HouseNumber = "5", PostalCode = "2030", City = "Antwerpen", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "TMK-001", Name = "TEST MASTER Klant", VatNumber = "BE0888777049",
            Street = "Masterstraat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen", IsActive = true,
        });
        db.Context.UnitTypes.Add(new UnitType { Id = unitId, TenantId = tenantId, Code = "TMKIST", Name = "TEST MASTER Kist", IsActive = true });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssens",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-TMK-001", IsActive = true });
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0101", OrderDate = new DateOnly(2026, 10, 17),
            Status = TransportOrderStatus.Completed, LegalEntityId = entityId, CustomerReference = "PO-TMK-0002",
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, LocationName = "Laadadres A",
            Address = "Laadkaai 10", PostalCode = "2000", City = "Antwerpen", CountryCode = "BE",
        });
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, LocationName = "Losadres B",
            Address = "Loskaai 20", PostalCode = "9000", City = "Gent", CountryCode = "BE",
        });
        db.Context.TransportOrders.Add(order);
        db.Context.CargoItems.Add(new CargoItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id, Sequence = 1,
            Description = "Kisten onderdelen", ExpectedQuantity = 4, QuantityUnitCode = "TMKIST", TotalWeightKg = 120,
        });
        var trip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0063", TripDate = new DateOnly(2026, 10, 17),
            Status = TripStatus.Completed, DriverId = driverId, VehicleId = vehicleId,
        };
        db.Context.Trips.Add(trip);
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = trip.Id, TransportOrderId = order.Id, Sequence = 1 });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, order.Id, customerId, entityId, vehicleId, unitId, employeeId);
    }

    /// <summary>Every master record the document prints, changed after issue.</summary>
    private static async Task MutateMasterDataAsync(Harness h)
    {
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        customer.Name = "TEST MASTER Klant HERNOEMD";
        customer.VatNumber = "BE0777666519";
        customer.Street = "Nieuwe Masterlaan";
        customer.HouseNumber = "77";
        var entity = await h.Db.Context.LegalEntities.SingleAsync(e => e.Id == h.LegalEntityId);
        entity.LegalName = "HERNOEMD NV";
        entity.VatNumber = "BE0888777148";
        entity.Street = "Transportlaan";
        entity.HouseNumber = "500";
        (await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId)).LicensePlate = "1-TMK-901";
        var employee = await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeId);
        employee.FirstName = "Piet";
        // The unit is renamed, then deleted and its code re-created with another meaning.
        var unit = await h.Db.Context.UnitTypes.SingleAsync(u => u.Id == h.UnitId);
        unit.Name = "TEST MASTER Kist HERNOEMD";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.UnitTypes.Remove(unit);
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "TMKIST", Name = "Vat ANDERE BETEKENIS", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Cmr")]
    [InlineData("DeliveryNote")]
    public async Task IssuedDocument_IsByteIdentical_AfterEveryMasterDataChange(string kind)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = (await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest(kind, Guid.NewGuid()), CancellationToken.None))!;
        var before = (await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value;

        // The snapshot carries what was printed at issue: the unit LABEL, not the code.
        Assert.Equal("TEST MASTER Kist", before.Snapshot.Lines.Single().Unit);
        Assert.Equal("TEST MASTER Klant", before.Snapshot.Customer.Name);
        Assert.Equal("1-TMK-001", before.Snapshot.Consignment!.VehicleRegistration);
        Assert.Equal("Jan Janssens", before.Snapshot.Consignment.DriverName);

        await MutateMasterDataAsync(h);
        h.Db.Context.ChangeTracker.Clear();
        var after = (await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value;

        Assert.Equal(before.Content, after.Content);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(before.Snapshot),
            System.Text.Json.JsonSerializer.Serialize(after.Snapshot));
        Assert.Equal("Acme Transport BV", after.Snapshot.Seller.Name);
        Assert.Equal("BE0123456749", after.Snapshot.Seller.VatNumber);
        Assert.Equal("BE0888777049", after.Snapshot.Customer.VatNumber);
        Assert.Equal("TEST MASTER Kist", after.Snapshot.Lines.Single().Unit);
    }

    [Fact]
    public async Task NewDocument_UsesTheNewMasterData_TheOldOneStaysAsIssued()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = (await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None))!;
        await MutateMasterDataAsync(h);

        var replacement = (await h.Sut().IssueAsync(h.OrderId,
            new IssueTransportDocumentRequest("Cmr", Guid.NewGuid(), null, first.Id, "Nieuwe nummerplaat"), CancellationToken.None))!;

        var old = (await h.Sut().RenderPdfAsync(first.Id, CancellationToken.None))!.Value.Snapshot;
        var current = (await h.Sut().RenderPdfAsync(replacement.Id, CancellationToken.None))!.Value.Snapshot;
        Assert.Equal(("1-TMK-001", "Acme Transport BV", "TEST MASTER Kist"),
            (old.Consignment!.VehicleRegistration, old.Consignment.Carrier.Name, old.Lines.Single().Unit));
        Assert.Equal(("1-TMK-901", "HERNOEMD NV", "Vat ANDERE BETEKENIS"),
            (current.Consignment!.VehicleRegistration, current.Consignment.Carrier.Name, current.Lines.Single().Unit));
        Assert.Equal("CMR-2026-00002", old.Reference!.ReplacedByNumber); // the lifecycle marker is not content
    }

    [Fact]
    public async Task TripBatch_ReprintsTheIssuedDocument_NotTheLiveData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None);
        await MutateMasterDataAsync(h);
        var tripId = await h.Db.Context.Trips.Select(t => t.Id).SingleAsync();

        var batch = (await new TransportDocumentService(h.Db.Context, new DevTenantContext(h.TenantId))
            .RenderTripBatchAsync(tripId, "cmr", CancellationToken.None))!.Value;
        var text = PdfText(batch.Content);
        Assert.Contains("1-TMK-001", text);
        Assert.DoesNotContain("1-TMK-901", text);
        Assert.Contains("CMR-2026-00001", text);
    }

    /// <summary>
    /// MD-B1 follow-up: the order header's CMR / LB action. Without an issued document it is a
    /// stamped CONCEPT (number-less, live); once issued, the action resolves to that immutable,
    /// numbered document — after a replacement, to the successor.
    /// </summary>
    [Fact]
    public async Task OrderDocumentAction_IsAStampedConcept_UntilIssued_ThenTheActiveIssuedDocument()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var documents = new TransportDocumentService(h.Db.Context, new DevTenantContext(h.TenantId));

        Assert.Null(await h.Sut().FindActiveAsync(h.OrderId, "cmr", CancellationToken.None));
        var concept = (await documents.RenderAsync(h.OrderId, "cmr", CancellationToken.None))!.Value;
        Assert.StartsWith("concept-", concept.FileName);
        var conceptText = PdfText(concept.Content);
        Assert.Contains("CONCEPT", conceptText);
        Assert.Contains("geen officieel document", conceptText);
        Assert.DoesNotContain("CMR-2026", conceptText);

        var first = (await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None))!;
        Assert.Equal(first.Id, await h.Sut().FindActiveAsync(h.OrderId, "cmr", CancellationToken.None));
        Assert.Null(await h.Sut().FindActiveAsync(h.OrderId, "delivery-note", CancellationToken.None));
        var official = (await h.Sut().RenderPdfAsync(first.Id, CancellationToken.None))!.Value;
        var officialText = PdfText(official.Content);
        Assert.Contains("CMR-2026-00001", officialText);
        Assert.DoesNotContain("CONCEPT", officialText);

        var second = (await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid(),
            ReplacesDocumentId: first.Id, ReplacementReason: "Correctie"), CancellationToken.None))!;
        Assert.Equal(second.Id, await h.Sut().FindActiveAsync(h.OrderId, "cmr", CancellationToken.None));
    }

    [Fact]
    public async Task LegacyDocument_WithoutSnapshot_IsFrozenOnce_ThenNeverChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = (await h.Sut().IssueAsync(h.OrderId, new IssueTransportDocumentRequest("Cmr", Guid.NewGuid()), CancellationToken.None))!;
        // Simulate a document issued before MD-B1: no stored content.
        await h.Db.Context.IssuedTransportDocuments.Where(d => d.Id == issued.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SnapshotJson, (string?)null).SetProperty(d => d.SnapshotVersion, (int?)null));
        h.Db.Context.ChangeTracker.Clear();

        Assert.Equal(1, await IssuedTransportDocumentSnapshotBackfill.SyncAsync(h.Db.Context));
        Assert.Equal(0, await IssuedTransportDocumentSnapshotBackfill.SyncAsync(h.Db.Context)); // idempotent
        var stored = await h.Db.Context.IssuedTransportDocuments.AsNoTracking().SingleAsync(d => d.Id == issued.Id);
        Assert.True(stored.SnapshotIsBackfilled);
        var frozen = (await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value;

        await MutateMasterDataAsync(h);
        h.Db.Context.ChangeTracker.Clear();
        Assert.Equal(frozen.Content, (await h.Sut().RenderPdfAsync(issued.Id, CancellationToken.None))!.Value.Content);
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        return string.Concat(document.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .Select(p => System.Text.Encoding.Latin1.GetString(p.Contents.CreateSingleContent().Stream.UnfilteredValue)));
    }
}
