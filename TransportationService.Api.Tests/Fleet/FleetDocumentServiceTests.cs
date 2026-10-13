using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class FleetDocumentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(SqliteTestDbContext Db, FleetDocumentService Sut, Guid TenantId, Guid VehicleId, Guid TrailerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new FleetDocumentService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ts-tests", System.Guid.NewGuid().ToString("N"))));
        return new Harness(db, sut, tenantId, vehicleId, trailerId);
    }

    private static CreateFleetDocumentRequest Request(
        FleetDocumentType type = FleetDocumentType.Insurance, DateOnly? expiry = null, int? warningDays = null,
        string? customName = null, DateOnly? issue = null) =>
        new(type, customName, "DOC-1", issue, expiry, warningDays, null);

    [Fact]
    public async Task Create_ForVehicleAndTrailer_ListsPerOwner()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(FleetDocumentType.Registration), CancellationToken.None);
        await h.Sut.CreateForTrailerAsync(h.TrailerId, Request(FleetDocumentType.TechnicalInspection), CancellationToken.None);

        var vehicleDocs = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);
        var trailerDocs = await h.Sut.ListForTrailerAsync(h.TrailerId, CancellationToken.None);

        Assert.Single(vehicleDocs!);
        Assert.Equal(FleetDocumentType.Registration, vehicleDocs![0].DocumentType);
        Assert.Single(trailerDocs!);
        Assert.Equal(FleetDocumentType.TechnicalInspection, trailerDocs![0].DocumentType);
    }

    [Fact]
    public async Task Create_ForForeignVehicle_ReturnsOwnerNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateForVehicleAsync(foreignVehicle, Request(), CancellationToken.None);

        Assert.Equal(FleetDocumentOperationOutcome.OwnerNotFound, result.Outcome);
    }

    [Fact]
    public async Task ListForVehicle_ForeignVehicle_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ListForVehicleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null, null, FleetDocumentStatus.NoExpiry)]
    [InlineData("2026-07-17", null, FleetDocumentStatus.Expired)]       // yesterday
    [InlineData("2026-07-18", null, FleetDocumentStatus.ExpiringSoon)]  // today, inside window
    [InlineData("2026-09-10", null, FleetDocumentStatus.ExpiringSoon)]  // inside default 60d
    [InlineData("2026-12-01", null, FleetDocumentStatus.Valid)]         // outside default 60d
    [InlineData("2026-08-01", 5, FleetDocumentStatus.Valid)]            // custom 5d window: not yet
    [InlineData("2026-07-20", 5, FleetDocumentStatus.ExpiringSoon)]     // custom 5d window: inside
    public void ComputeStatus_CoversAllBoundaries(string? expiry, int? warningDays, FleetDocumentStatus expected)
    {
        var expiryDate = expiry is null ? (DateOnly?)null : DateOnly.Parse(expiry);

        Assert.Equal(expected, FleetDocumentService.ComputeStatus(expiryDate, warningDays, Today));
    }

    [Fact]
    public async Task Create_OtherTypeWithoutName_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId,
            Request(FleetDocumentType.Other), CancellationToken.None);

        Assert.Equal(FleetDocumentOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Create_ExpiryBeforeIssue_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId,
            Request(expiry: new DateOnly(2026, 1, 1), issue: new DateOnly(2026, 6, 1)), CancellationToken.None);

        Assert.Equal(FleetDocumentOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task ListExpiring_ReturnsUrgentFirst_WithOwnerInfo_AndSkipsFarFuture()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(FleetDocumentType.Insurance, expiry: new DateOnly(2026, 8, 1)), CancellationToken.None);
        await h.Sut.CreateForTrailerAsync(h.TrailerId, Request(FleetDocumentType.AdrCertificate, expiry: new DateOnly(2026, 7, 1)), CancellationToken.None); // already expired
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(FleetDocumentType.Conformity, expiry: new DateOnly(2027, 7, 1)), CancellationToken.None);     // far future

        var expiring = await h.Sut.ListExpiringAsync(60, CancellationToken.None);

        Assert.Equal(2, expiring.Count);
        Assert.Equal(FleetDocumentType.AdrCertificate, expiring[0].DocumentType);
        Assert.Equal(FleetDocumentStatus.Expired, expiring[0].Status);
        Assert.Equal("OPL-0001", expiring[0].OwnerNumber);
        Assert.Equal(FleetDocumentType.Insurance, expiring[1].DocumentType);
        Assert.Equal("VRT-0001", expiring[1].OwnerNumber);
    }

    [Fact]
    public async Task ListExpiring_DoesNotLeakOtherTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.FleetDocuments.Add(new FleetDocument
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle,
            DocumentType = FleetDocumentType.Insurance, ExpiryDate = new DateOnly(2026, 7, 1),
        });
        await h.Db.Context.SaveChangesAsync();

        var expiring = await h.Sut.ListExpiringAsync(60, CancellationToken.None);

        Assert.Empty(expiring);
    }

    [Fact]
    public async Task UpdateAndDelete_Work()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(), CancellationToken.None);

        var updated = await h.Sut.UpdateAsync(created.Document!.Id, new UpdateFleetDocumentRequest(
            FleetDocumentType.Other, "Tolbadge", "DOC-2", null, new DateOnly(2026, 12, 31), 14, "note"), CancellationToken.None);

        Assert.Equal(FleetDocumentOperationOutcome.Success, updated.Outcome);
        Assert.Equal("Tolbadge", updated.Document!.CustomTypeName);
        Assert.Equal(14, updated.Document.WarningDays);

        Assert.True(await h.Sut.DeleteAsync(created.Document.Id, CancellationToken.None));
        var docs = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);
        Assert.Empty(docs!);
    }

    [Fact]
    public async Task UploadDownloadRemove_Attachment_Roundtrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(FleetDocumentType.LeasingContract), CancellationToken.None);
        var id = created.Document!.Id;

        using var upload = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var attached = await h.Sut.AttachFileAsync(id, "contract.pdf", "application/pdf", upload, CancellationToken.None);
        Assert.True(attached.Document!.HasAttachment);

        var opened = await h.Sut.OpenFileAsync(id, CancellationToken.None);
        Assert.NotNull(opened);
        await opened!.Value.Content.DisposeAsync();

        var removed = await h.Sut.RemoveFileAsync(id, CancellationToken.None);
        Assert.False(removed.Document!.HasAttachment);
        Assert.Null(await h.Sut.OpenFileAsync(id, CancellationToken.None));
    }
}
