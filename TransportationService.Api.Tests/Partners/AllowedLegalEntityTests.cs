using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

/// <summary>
/// Wave 2 §2 (spec Part O, scenario 17): allowed issuing entities per customer. Empty set =
/// everything allowed (backward compatible); non-empty: the customer default must be inside,
/// explicit dossier/order/invoice choices outside it are validation errors, and moving a
/// dossier to a NON-default entity is a separate audited right (dossiers.override_entity,
/// roles v27) with a mandatory reason.
/// </summary>
public class AllowedLegalEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid CustomerId,
        Guid EntityAId, Guid EntityBId, Guid EntityCId, PermissionSet Permissions)
    {
        public CustomerService Customers()
        {
            var tenant = new DevTenantContext(TenantId);
            return new CustomerService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(UserId)),
                new CountryCodeValidator(Db.Context));
        }

        public DossierService Dossiers()
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(UserId);
            return new DossierService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, user), new TestClock(Now),
                permissionService: Permissions, currentUser: user);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var entityC = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.LegalEntities.AddRange(
            new LegalEntity { Id = entityA, TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true },
            new LegalEntity { Id = entityB, TenantId = tenantId, LegalName = "Acme Logistics BV", IsActive = true },
            new LegalEntity { Id = entityC, TenantId = tenantId, LegalName = "Acme Kranen BV", IsActive = true });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV",
            IsActive = true, DefaultLegalEntityId = entityA,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, customerId, entityA, entityB, entityC, new PermissionSet());
    }

    private static void AllowEntities(Harness h, params Guid[] entityIds)
    {
        foreach (var entityId in entityIds)
        {
            h.Db.Context.CustomerAllowedLegalEntities.Add(new CustomerAllowedLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, LegalEntityId = entityId,
            });
        }
        h.Db.Context.SaveChanges();
    }

    // --- Customer admin ---------------------------------------------------------------------

    private static UpdateCustomerRequest UpdateRequest(
        Guid? defaultEntity, IReadOnlyList<Guid>? allowed, string? invoiceGrouping = null) => new(
        "Klant BV", null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null, true,
        DefaultLegalEntityId: defaultEntity, AllowedLegalEntityIds: allowed, InvoiceGrouping: invoiceGrouping);

    [Fact]
    public async Task Customer_SavesAllowedSet_AndRequiresTheDefaultInsideIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customers = h.Customers();

        var detail = await customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityAId, [h.EntityAId, h.EntityBId]), CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.AllowedLegalEntityIds!.Count);
        Assert.Contains(h.EntityAId, detail.AllowedLegalEntityIds);
        Assert.Contains(h.EntityBId, detail.AllowedLegalEntityIds);

        // The default must stay inside the (new) set.
        await Assert.ThrowsAsync<DomainValidationException>(() => customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityCId, [h.EntityAId, h.EntityBId]), CancellationToken.None));

        // Emptying the list clears the restriction again.
        var cleared = await customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityCId, []), CancellationToken.None);
        Assert.Empty(cleared!.AllowedLegalEntityIds!);
    }

    [Fact]
    public async Task Customer_RejectsForeignOrUnknownAllowedEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Customers().UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityAId, [h.EntityAId, Guid.NewGuid()]), CancellationToken.None));
    }

    /// <summary>Wave 2 §4: the grouping preference stores/round-trips; storage only until Wave 10.</summary>
    [Fact]
    public async Task Customer_RoundTripsInvoiceGrouping_AndRejectsUnknownValues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customers = h.Customers();

        var detail = await customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityAId, null, invoiceGrouping: "PerDossier"), CancellationToken.None);
        Assert.Equal("PerDossier", detail!.InvoiceGrouping);

        // Omitting the field leaves the stored preference untouched.
        var untouched = await customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityAId, null), CancellationToken.None);
        Assert.Equal("PerDossier", untouched!.InvoiceGrouping);

        await Assert.ThrowsAsync<DomainValidationException>(() => customers.UpdateAsync(h.CustomerId,
            UpdateRequest(h.EntityAId, null, invoiceGrouping: "Fortnightly"), CancellationToken.None));
    }

    // --- Dossier move (scenario 17) ---------------------------------------------------------

    private static async Task<Guid> CreateDossierAsync(Harness h)
    {
        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest("Project X", CustomerId: h.CustomerId), CancellationToken.None);
        return dossier.Id;
    }

    [Fact]
    public async Task DossierMove_ToNonDefault_RequiresOverridePermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossierId = await CreateDossierAsync(h);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityBId, Reason: "Klant vraagt entiteit B"),
            CancellationToken.None));
        Assert.Contains("geen rechten", ex.Message);
    }

    [Fact]
    public async Task DossierMove_ToNonDefault_RequiresAReason_AndAuditsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var dossierId = await CreateDossierAsync(h);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityBId), CancellationToken.None));
        Assert.Contains("reden", ex.Message, StringComparison.OrdinalIgnoreCase);

        var moved = await h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityBId, Reason: "Klant vraagt entiteit B"),
            CancellationToken.None);
        Assert.Equal(h.EntityBId, moved!.LegalEntityId);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityId == dossierId.ToString() && a.Action == "LegalEntityChanged")
            .SingleAsync();
        Assert.Contains("Klant vraagt entiteit B", audit.NewValuesJson);
    }

    [Fact]
    public async Task DossierMove_BackToCustomerDefault_NeedsNoOverrideRight()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        var dossierId = await CreateDossierAsync(h);
        await h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityBId, Reason: "Test"), CancellationToken.None);

        h.Permissions.Codes.Clear();
        var restored = await h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityAId), CancellationToken.None);
        Assert.Equal(h.EntityAId, restored!.LegalEntityId);
    }

    [Fact]
    public async Task DossierMove_OutsideAllowedSet_IsRejected_NamingTheAllowedEntities()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.DossiersOverrideEntity);
        AllowEntities(h, h.EntityAId, h.EntityBId);
        var dossierId = await CreateDossierAsync(h);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Dossiers().ChangeLegalEntityAsync(
            dossierId, new ChangeDossierEntityRequest(h.EntityCId, Reason: "Poging"), CancellationToken.None));
        Assert.Contains("Acme Transport BV", ex.Message);
        Assert.Contains("Acme Logistics BV", ex.Message);
        Assert.DoesNotContain("Acme Kranen BV", ex.Message);
    }
}
