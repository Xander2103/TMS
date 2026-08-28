using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Accounting;

/// <summary>
/// Corrections wave §7.1/§7.2: tenant-scoped ledger accounts and sales-category mappings —
/// unique numbers per tenant, audited mapping changes with old → new account, inactive
/// accounts never newly assignable, delete blocked while referenced, health lists unmapped.
/// </summary>
public class AccountingServiceTests
{
    private sealed record Harness(SqliteTestDbContext Db, AccountingService Service, Guid TenantId);

    private static async Task<Harness> SetupAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var service = new AccountingService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
        return new Harness(db, service, tenantId);
    }

    [Fact]
    public async Task Seeding_CreatesDefaultDutchCategories_WithSystemRoles()
    {
        var h = await SetupAsync();
        using var _ = h.Db;

        var categories = await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None);

        Assert.Equal(6, categories.Count);
        Assert.Equal(SalesCategorySystemRole.Transport, categories.Single(c => c.Code == "TRANSPORT").SystemRole);
        Assert.Equal(SalesCategorySystemRole.Surcharge, categories.Single(c => c.Code == "SUPPLEMENTEN").SystemRole);
        Assert.Equal(SalesCategorySystemRole.Diesel, categories.Single(c => c.Code == "DIESEL").SystemRole);
        Assert.All(categories, c => Assert.Null(c.LedgerAccountId)); // nothing hardcoded, ever
    }

    [Fact]
    public async Task AccountNumbers_AreUniquePerTenant_NotAcrossTenants()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Transportopbrengsten"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Dubbel"), CancellationToken.None));

        // Company B configures its own 700000 without any clash.
        var tenantB = new DevTenantContext(Guid.NewGuid());
        var serviceB = new AccountingService(h.Db.Context, tenantB, new AuditService(h.Db.Context, tenantB, new DevCurrentUserContext(null)));
        var accountB = await serviceB.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Omzet"), CancellationToken.None);
        Assert.Equal("700000", accountB.AccountNumber);

        // And neither tenant sees the other's accounts.
        Assert.Single(await h.Service.ListLedgerAccountsAsync(true, CancellationToken.None));
        Assert.Single(await serviceB.ListLedgerAccountsAsync(true, CancellationToken.None));
    }

    [Fact]
    public async Task MappingChange_IsAudited_WithOldAndNewAccount()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var oldAccount = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Transportopbrengsten"), CancellationToken.None);
        var newAccount = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700100", "Transporttoeslagen"), CancellationToken.None);
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");

        await h.Service.UpdateSalesCategoryAsync(transport.Id, new SaveSalesCategoryRequest(
            transport.Code, transport.Name, transport.SystemRole, oldAccount.Id, true, transport.SortOrder), CancellationToken.None);
        var mapped = await h.Service.UpdateSalesCategoryAsync(transport.Id, new SaveSalesCategoryRequest(
            transport.Code, transport.Name, transport.SystemRole, newAccount.Id, true, transport.SortOrder), CancellationToken.None);

        Assert.Equal("700100", mapped!.LedgerAccountNumber);
        var audits = await h.Db.Context.AuditLogs
            .Where(l => l.TenantId == h.TenantId && l.EntityType == "SalesCategory" && l.Action == "Updated")
            .OrderBy(l => l.Timestamp).ToListAsync();
        var last = audits.Last();
        Assert.Contains("700000", last.OldValuesJson);
        Assert.Contains("700100", last.NewValuesJson);
    }

    [Fact]
    public async Task InactiveAccount_CannotBeNewlyAssigned_ButExistingMappingSurvives()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var account = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700200", "Dieseltoeslagen"), CancellationToken.None);
        var diesel = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "DIESEL");
        await h.Service.UpdateSalesCategoryAsync(diesel.Id, new SaveSalesCategoryRequest(
            diesel.Code, diesel.Name, diesel.SystemRole, account.Id, true, diesel.SortOrder), CancellationToken.None);

        await h.Service.UpdateLedgerAccountAsync(account.Id, new SaveLedgerAccountRequest("700200", "Dieseltoeslagen", IsActive: false), CancellationToken.None);

        // The existing mapping keeps rendering …
        var still = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "DIESEL");
        Assert.Equal("700200", still.LedgerAccountNumber);

        // … but a NEW assignment of the inactive account is refused.
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(
                transport.Code, transport.Name, transport.SystemRole, account.Id, true, transport.SortOrder), CancellationToken.None));
    }

    [Fact]
    public async Task MappedAccount_DeleteIsBlocked_UnusedDeletes()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var mappedAccount = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Transportopbrengsten"), CancellationToken.None);
        var unusedAccount = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700999", "Tijdelijk"), CancellationToken.None);
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");
        await h.Service.UpdateSalesCategoryAsync(transport.Id, new SaveSalesCategoryRequest(
            transport.Code, transport.Name, transport.SystemRole, mappedAccount.Id, true, transport.SortOrder), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Service.DeleteLedgerAccountAsync(mappedAccount.Id, CancellationToken.None));
        Assert.Contains("kan niet worden verwijderd", ex.Message);

        Assert.True(await h.Service.DeleteLedgerAccountAsync(unusedAccount.Id, CancellationToken.None));
        Assert.DoesNotContain(await h.Service.ListLedgerAccountsAsync(true, CancellationToken.None), a => a.Id == unusedAccount.Id);
    }

    [Fact]
    public async Task Health_ListsExactlyTheUnmappedActiveCategories()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var account = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Transportopbrengsten"), CancellationToken.None);
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");
        await h.Service.UpdateSalesCategoryAsync(transport.Id, new SaveSalesCategoryRequest(
            transport.Code, transport.Name, transport.SystemRole, account.Id, true, transport.SortOrder), CancellationToken.None);

        var health = await h.Service.GetHealthAsync(CancellationToken.None);

        Assert.Equal(5, health.UnmappedCategories.Count);
        Assert.DoesNotContain(health.UnmappedCategories, c => c.Code == "TRANSPORT");
    }

    [Fact]
    public async Task OneActiveSystemRolePerTenant_IsEnforced()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None); // seeds

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Service.CreateSalesCategoryAsync(
            new SaveSalesCategoryRequest("TRANSPORT2", "Transport bis", SalesCategorySystemRole.Transport), CancellationToken.None));
    }

    // ------------------------------------------ audit: per-entity ledger mappings

    private static async Task<Guid> AddEntityAsync(Harness h)
    {
        var entity = new TransportationService.Api.Modules.Organization.Entities.LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, LegalName = "Entiteit", IsActive = true,
        };
        h.Db.Context.LegalEntities.Add(entity);
        await h.Db.Context.SaveChangesAsync();
        return entity.Id;
    }

    [Fact]
    public async Task LedgerMapping_MustReferenceAnOwnTenantAccountAndEntity()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var entityId = await AddEntityAsync(h);
        var ownAccount = await h.Service.CreateLedgerAccountAsync(new SaveLedgerAccountRequest("700000", "Transport"), CancellationToken.None);
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");

        // Another tenant's account: refused, never silently stored.
        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var foreignAccount = new LedgerAccount { Id = Guid.NewGuid(), TenantId = otherTenant, AccountNumber = "700000", Name = "Foreign" };
        h.Db.Context.LedgerAccounts.Add(foreignAccount);
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                LedgerMappings: [new SalesCategoryLedgerMappingDto(entityId, foreignAccount.Id, null)]), CancellationToken.None));

        // An unknown entity: refused.
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                LedgerMappings: [new SalesCategoryLedgerMappingDto(Guid.NewGuid(), ownAccount.Id, null)]), CancellationToken.None));

        // A valid mapping is stored, and the mapped account can no longer be deleted.
        var saved = await h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                LedgerMappings: [new SalesCategoryLedgerMappingDto(entityId, ownAccount.Id, "CC-1")]), CancellationToken.None);
        Assert.Single(saved.LedgerMappings!, m => m.LegalEntityId == entityId && m.LedgerAccountId == ownAccount.Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Service.DeleteLedgerAccountAsync(ownAccount.Id, CancellationToken.None));

        // Saving the same mapping again does not pile up soft-deleted twins.
        await h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                LedgerMappings: [new SalesCategoryLedgerMappingDto(entityId, ownAccount.Id, "CC-1")]), CancellationToken.None);
        var rows = await h.Db.Context.Set<SalesCategoryLedgerMapping>().IgnoreQueryFilters()
            .CountAsync(m => m.SalesCategoryId == transport.Id);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task CostCentre_LongerThanTheSnapshotColumn_IsRefusedUpFront()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                CostCentre: new string('X', 41)), CancellationToken.None));
    }

    [Fact]
    public async Task AStatutoryClassification_RefusesAContradictingLegacyCategory()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var transport = (await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == "TRANSPORT");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Service.UpdateSalesCategoryAsync(
            transport.Id, new SaveSalesCategoryRequest(transport.Code, transport.Name, transport.SystemRole, null, true, transport.SortOrder,
                VatCategoryOverride: "S",
                VatTreatmentOverride: TransportationService.Api.Modules.Partners.Entities.VatTreatment.ReverseCharge), CancellationToken.None));
    }

    [Fact]
    public async Task Seeding_FlagsTransportAndSupplements_ForTheDieselBase()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var categories = await h.Service.ListSalesCategoriesAsync(false, CancellationToken.None);
        Assert.True(categories.Single(c => c.Code == "TRANSPORT").IncludeInDieselBase);
        Assert.True(categories.Single(c => c.Code == "SUPPLEMENTEN").IncludeInDieselBase);
        Assert.False(categories.Single(c => c.Code == "DIESEL").IncludeInDieselBase);
    }
}
