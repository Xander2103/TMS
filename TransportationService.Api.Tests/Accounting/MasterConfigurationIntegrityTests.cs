using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Dtos;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Organization.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Accounting;

/// <summary>
/// Master-data remediation 2026-10-18 — accounting / configuration integrity:
/// MD-H1 (inline default-ledger edit wiped the category), MD-H2 (entity ledger mapping ignored at
/// issue), MD-H4/M14 (legal entity accepted invalid IBAN/BIC, duplicate Peppol id → 500, thin
/// audit), MD-M2 (silent last-write-wins), MD-M5 (negative / blank payment term).
/// </summary>
public class MasterConfigurationIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid EntityA, Guid EntityB, Guid Ledger709100, Guid Ledger709200,
        AccountingService Accounting, LegalEntityService Entities, InvoiceService Invoices, CustomerService Customers, AuditService Audit);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = $"t-{tenantId:N}", CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await Data.CountrySeeder.SyncAsync(db.Context);
        var entityA = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Entiteit A BV", VatNumber = "BE0123456749", CountryCode = "BE",
            InvoicePrefix = "A", InvoiceNumberFormat = "{PREFIX}{YYYY}{SEQ}", IsActive = true, IsDefault = true,
        };
        var entityB = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Entiteit B BV", VatNumber = "BE0417497106", CountryCode = "BE",
            InvoicePrefix = "B", InvoiceNumberFormat = "{PREFIX}{YYYY}{SEQ}", IsActive = true,
        };
        var l1 = new LedgerAccount { Id = Guid.NewGuid(), TenantId = tenantId, AccountNumber = "709100", Name = "Omzet A", IsActive = true };
        var l2 = new LedgerAccount { Id = Guid.NewGuid(), TenantId = tenantId, AccountNumber = "709200", Name = "Omzet B", IsActive = true };
        db.Context.LegalEntities.AddRange(entityA, entityB);
        db.Context.LedgerAccounts.AddRange(l1, l2);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var accounting = new AccountingService(db.Context, tenant, audit);
        var storage = new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-mci", Guid.NewGuid().ToString("N")));
        return new Harness(db, tenantId, entityA.Id, entityB.Id, l1.Id, l2.Id, accounting,
            new LegalEntityService(db.Context, tenant, user, audit, new CountryCodeValidator(db.Context), storage),
            new InvoiceService(db.Context, tenant, audit, clock, new InvoiceNumberService(db.Context, tenant),
                new CustomerBillingConfigService(db.Context, tenant, audit, clock), accounting),
            new CustomerService(db.Context, tenant, audit, new CountryCodeValidator(db.Context)), audit);
    }

    private static SaveSalesCategoryRequest FullCategory(Harness h) => new(
        "TMK-LE", "TEST Categorie", LedgerAccountId: h.Ledger709100,
        InvoiceDescriptionNl: "Transport NL", InvoiceDescriptionFr: "Transport FR", InvoiceDescriptionEn: "Transport EN",
        InvoiceDescriptionDe: "Transport DE", IncludeInDieselBase: true,
        VatTreatmentOverride: VatTreatment.ReverseCharge, CostCentre: "KP-01",
        LedgerMappings: [new SalesCategoryLedgerMappingDto(h.EntityB, h.Ledger709200, "KP-B")]);

    // ---- MD-H1 ----------------------------------------------------------------------------

    [Fact]
    public async Task InlineDefaultLedgerEdit_ChangesOnlyTheLedger_EverythingElseSurvives()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Accounting.CreateSalesCategoryAsync(FullCategory(h), CancellationToken.None);

        var updated = (await h.Accounting.SetSalesCategoryLedgerAsync(created.Id,
            new SetSalesCategoryLedgerRequest(h.Ledger709200, created.Version), CancellationToken.None))!;

        Assert.Equal(h.Ledger709200, updated.LedgerAccountId);
        // The LIST is what the inline control edits from: it must carry the current version, or the
        // next inline change is refused as stale (found in the browser regression).
        var listed = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Id == created.Id);
        Assert.Equal(updated.Version, listed.Version);
        Assert.NotNull(await h.Accounting.SetSalesCategoryLedgerAsync(created.Id,
            new SetSalesCategoryLedgerRequest(h.Ledger709100, listed.Version), CancellationToken.None));
        Assert.Equal(("Transport NL", "Transport FR", "Transport EN", "Transport DE"),
            (updated.InvoiceDescriptionNl, updated.InvoiceDescriptionFr, updated.InvoiceDescriptionEn, updated.InvoiceDescriptionDe));
        Assert.True(updated.IncludeInDieselBase);
        Assert.Equal(VatTreatment.ReverseCharge, updated.VatTreatmentOverride);
        Assert.Equal("KP-01", updated.CostCentre);
        var mapping = Assert.Single(updated.LedgerMappings!);
        Assert.Equal((h.EntityB, h.Ledger709200, "KP-B"), (mapping.LegalEntityId, mapping.LedgerAccountId, mapping.CostCentre));

        // The audit names the one field that changed, old → new.
        var audit = await h.Db.Context.AuditLogs.Where(a => a.EntityType == "SalesCategory" && a.Action == "Updated").OrderBy(a => a.Timestamp).FirstAsync();
        Assert.Contains("709100", audit.OldValuesJson);
        Assert.Contains("709200", audit.NewValuesJson);
        Assert.DoesNotContain("InvoiceDescriptionFr", audit.NewValuesJson);
    }

    [Fact]
    public async Task FullUpdate_WithoutMappingList_KeepsTheEntityMappings()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Accounting.CreateSalesCategoryAsync(FullCategory(h), CancellationToken.None);

        var updated = (await h.Accounting.UpdateSalesCategoryAsync(created.Id,
            FullCategory(h) with { Name = "Hernoemd", LedgerMappings = null, ExpectedVersion = created.Version }, CancellationToken.None))!;

        Assert.Equal("Hernoemd", updated.Name);
        Assert.Single(updated.LedgerMappings!);
    }

    // ---- MD-H2 ----------------------------------------------------------------------------

    private static async Task<InvoiceLine> IssueLineAsync(Harness h, Guid entityId, Guid categoryId)
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerNumber = $"KL-{Guid.NewGuid():N}"[..12], Name = "K", CountryCode = "BE", IsActive = true };
        h.Db.Context.Customers.Add(customer);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = customer.Id, LegalEntityId = entityId,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 10, InvoiceDate = new DateOnly(2026, 10, 18), DueDate = new DateOnly(2026, 11, 17),
            Status = InvoiceStatus.Draft, CustomerVatTreatment = "DomesticVat",
            Lines = [new InvoiceLine { Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "Kisten", Quantity = 1, UnitPrice = 100, VatRatePercent = 21, SalesCategoryId = categoryId }],
        };
        h.Db.Context.Invoices.Add(invoice);
        await h.Db.Context.SaveChangesAsync();
        var issued = await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.True(issued.Outcome == Modules.Invoicing.Dtos.InvoiceOperationOutcome.Success, issued.Error);
        h.Db.Context.ChangeTracker.Clear();
        return await h.Db.Context.InvoiceLines.AsNoTracking().SingleAsync(l => l.InvoiceId == invoice.Id);
    }

    [Fact]
    public async Task Issue_FreezesTheEntityMapping_OverTheCategoryDefault_AndNeverRewritesIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var category = await h.Accounting.CreateSalesCategoryAsync(
            new SaveSalesCategoryRequest("TMK-LE", "TEST", LedgerAccountId: h.Ledger709100,
                LedgerMappings: [new SalesCategoryLedgerMappingDto(h.EntityB, h.Ledger709200, null)]), CancellationToken.None);

        var mapped = await IssueLineAsync(h, h.EntityB, category.Id);
        var unmapped = await IssueLineAsync(h, h.EntityA, category.Id);
        Assert.Equal("709200", mapped.LedgerAccountNumberSnapshot);
        Assert.Equal("709100", unmapped.LedgerAccountNumberSnapshot);

        // A later mapping change never reaches the issued line.
        var current = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Id == category.Id);
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest("TMK-LE", "TEST", LedgerAccountId: h.Ledger709100,
            LedgerMappings: [new SalesCategoryLedgerMappingDto(h.EntityB, h.Ledger709100, null)], ExpectedVersion: current.Version), CancellationToken.None);
        Assert.Equal("709200", (await h.Db.Context.InvoiceLines.AsNoTracking().SingleAsync(l => l.Id == mapped.Id)).LedgerAccountNumberSnapshot);
    }

    // ---- MD-H4 / MD-M14 -------------------------------------------------------------------

    private static SaveLegalEntityRequest EntityRequest(string name, string? iban = null, string? bic = null,
        string? peppolScheme = null, string? peppolId = null, int term = 30, int? expectedVersion = null) =>
        new(name, null, null, null, peppolId, peppolScheme, "Transportlaan", "5", "2030", "Antwerpen", "BE",
            null, null, null, iban, bic, null, "EUR", term, "{PREFIX}{YYYY}{SEQ}", 4, "C" + name.Replace(" ", ""), null, false, ExpectedVersion: expectedVersion);

    [Theory]
    [InlineData("BE00123456789012", null, "iban")]
    [InlineData(null, "XX", "bic")]
    public async Task LegalEntity_RefusesAnInvalidIbanOrBic(string? iban, string? bic, string field)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Entities.CreateAsync(EntityRequest("TEST LE", iban, bic), CancellationToken.None));
        Assert.True(refused.FieldErrors!.ContainsKey(field));
    }

    [Fact]
    public async Task LegalEntity_DuplicatePeppolId_IsAClearValidationError_NotA500()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Entities.CreateAsync(EntityRequest("Eerste", peppolScheme: "0208", peppolId: "0888777148"), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Entities.CreateAsync(EntityRequest("Tweede", peppolScheme: "0208", peppolId: "0888777148"), CancellationToken.None));
        Assert.Contains("al in gebruik door Eerste", refused.Message);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Entities.CreateAsync(EntityRequest("Derde", peppolScheme: "0208"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Entities.CreateAsync(EntityRequest("Vierde", term: -5), CancellationToken.None));
    }

    [Fact]
    public async Task LegalEntity_WhoseNumbersWouldCollideWithAnotherEntity_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Entities.CreateAsync(EntityRequest("TEST LE") with { InvoicePrefix = "A" }, CancellationToken.None));
        Assert.Contains("dezelfde nummers als dat van Entiteit A BV", refused.Message);
    }

    [Fact]
    public async Task LegalEntity_AuditNamesEveryLegalField_AndAStaleEditIsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Entities.CreateAsync(EntityRequest("TEST LE"), CancellationToken.None);
        var updated = (await h.Entities.UpdateAsync(created.Id,
            EntityRequest("TEST LE", "BE62510007547061", "GEBABEBB", "0208", "0888777148", 21, created.Version), CancellationToken.None))!;

        var audit = await h.Db.Context.AuditLogs.Where(a => a.EntityType == "LegalEntity" && a.Action == "Updated").SingleAsync();
        foreach (var field in new[] { "Iban", "Bic", "PeppolId", "PeppolScheme", "PaymentTermDays" })
        {
            Assert.Contains(field, audit.NewValuesJson);
        }

        Assert.DoesNotContain("LegalName", audit.NewValuesJson); // unchanged → not noise

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            h.Entities.UpdateAsync(created.Id, EntityRequest("Stale", expectedVersion: created.Version), CancellationToken.None));
        Assert.Equal(created.Version + 1, updated.Version);
    }

    // ---- MD-M8: permission anomalies (role templates v38) ----------------------------------

    [Fact]
    public void RoleTemplates_V38_PlannerLosesFiscal_BoekhoudingGetsTheAddressPicker()
    {
        var planner = Data.DefaultRoleDefinitions.All.Single(t => t.Code == "planner");
        var boekhouding = Data.DefaultRoleDefinitions.All.Single(t => t.Code == "boekhouding");
        Assert.DoesNotContain(Modules.Identity.PermissionCodes.CustomersManageFiscal, planner.PermissionCodes);
        Assert.Contains(Modules.Identity.PermissionCodes.CustomersManagePo, planner.PermissionCodes); // intentional (intake)
        Assert.Contains(Modules.Identity.PermissionCodes.LocationsView, boekhouding.PermissionCodes);

        var step = Data.DefaultRoleUpgrades.Steps.Single(s => s.Version == 38);
        Assert.Contains(Modules.Identity.PermissionCodes.LocationsView, step.GrantsByTemplateCode["boekhouding"]);
        Assert.Contains(Modules.Identity.PermissionCodes.CustomersManageFiscal, step.RevocationsByTemplateCode!["planner"]);

        // Unit master edits need unit_types.manage; tariffs.manage only reaches the usage flags.
        static IReadOnlyList<string> Codes(string action) =>
            typeof(Modules.Reference.Controllers.UnitTypesController).GetMethod(action)!
                .GetCustomAttributes(typeof(Modules.Identity.Authorization.RequirePermissionAttribute), false)
                .Cast<Modules.Identity.Authorization.RequirePermissionAttribute>().Single().PermissionCodes;
        Assert.Equal(new[] { Modules.Identity.PermissionCodes.UnitTypesManage }, Codes("CreateMaster"));
        Assert.Equal(new[] { Modules.Identity.PermissionCodes.UnitTypesManage }, Codes("UpdateMaster"));
        Assert.Contains(Modules.Identity.PermissionCodes.TariffsManage, Codes("SaveSettings"));
    }

    // ---- MD-M4: default billing address -----------------------------------------------------

    [Fact]
    public async Task Issue_UsesTheCustomersDefaultBillingAddress_WhenOneIsMarked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var category = await h.Accounting.CreateSalesCategoryAsync(new SaveSalesCategoryRequest("TMK", "TEST", LedgerAccountId: h.Ledger709100), CancellationToken.None);
        var line = await IssueLineAsync(h, h.EntityA, category.Id);
        var invoiceId = line.InvoiceId;
        var customerId = (await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).CustomerId;
        var locationId = Guid.NewGuid();
        h.Db.Context.Locations.Add(new Modules.Locations.Entities.Location
        {
            Id = locationId, TenantId = h.TenantId, Name = "Facturatie", Street = "Factuurlaan", HouseNumber = "30", PostalCode = "2000", City = "Antwerpen", CountryCode = "BE", IsActive = true,
        });
        h.Db.Context.CustomerLocationLinks.Add(new Modules.Locations.Entities.CustomerLocationLink
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = customerId, LocationId = locationId, IsDefaultBilling = true, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        // The already-issued invoice is final; a NEW one picks up the billing address.
        Assert.NotEqual("Factuurlaan", (await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).CustomerStreetSnapshot);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = customerId, LegalEntityId = h.EntityA,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 10, InvoiceDate = new DateOnly(2026, 10, 18), DueDate = new DateOnly(2026, 11, 17),
            Status = InvoiceStatus.Draft, CustomerVatTreatment = "DomesticVat",
            Lines = [new InvoiceLine { Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "X", Quantity = 1, UnitPrice = 10, VatRatePercent = 21, SalesCategoryId = category.Id }],
        };
        h.Db.Context.Invoices.Add(invoice);
        await h.Db.Context.SaveChangesAsync();
        await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);
        var issued = await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(("Factuurlaan", "30", "Antwerpen"), (issued.CustomerStreetSnapshot, issued.CustomerHouseNumberSnapshot, issued.CustomerCitySnapshot));
    }

    // ---- MD-M2 / MD-M5: customer ----------------------------------------------------------

    private static UpdateCustomerRequest CustomerUpdate(CustomerDetailDto c, string? phone, string? website, int? term, int? expectedVersion) => new(
        c.Name, c.LegalName, c.VatNumber, c.CategoryId, c.Email, phone, website, c.Street, c.HouseNumber, c.PostalCode, c.City,
        c.CountryCode, c.InvoiceEmail, term, c.DefaultLanguageCode, c.Notes, c.IsActive, ExpectedVersion: expectedVersion);

    [Fact]
    public async Task Customer_SecondStaleSave_IsRefused_InsteadOfErasingTheFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Customers.CreateAsync(new CreateCustomerRequest(
            "TEST CONC", null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null), CancellationToken.None));
        // Two users opened the same version.
        var a = created;
        var b = created;

        var savedA = (await h.Customers.UpdateAsync(a.Id, CustomerUpdate(a, "+32 3 111 11 11", null, 30, a.Version), CancellationToken.None))!;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            h.Customers.UpdateAsync(b.Id, CustomerUpdate(b, null, "https://b.example", 30, b.Version), CancellationToken.None));

        h.Db.Context.ChangeTracker.Clear();
        var stored = (await h.Customers.GetByIdAsync(a.Id, CancellationToken.None))!;
        Assert.Equal("+32 3 111 11 11", stored.PhoneNumber); // A's change survives
        Assert.Null(stored.Website);
        Assert.Equal(savedA.Version, stored.Version);
    }

    [Fact]
    public async Task Customer_BlankPaymentTerm_FallsBack_NegativeIsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Customers.CreateAsync(new CreateCustomerRequest(
            "TEST TERM", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null), CancellationToken.None));
        Assert.Equal(0, created.PaymentTermDays); // 0 = not configured → legal entity / tenant term applies

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Customers.UpdateAsync(created.Id, CustomerUpdate(created, null, null, -5, created.Version), CancellationToken.None));
    }
}
