using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Warehouse / inventory remediation (acceptance 2026-10-18): delete integrity (WH-B1), quantity
/// bounds (WH-H1), serial identity (WH-H2), warehouse issue/return + loans (WH-H3/H4), offboarding
/// summary (WH-H5) and M1–M8.
/// </summary>
public class WarehouseRemediationTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, IssuedItemService Items, InventoryService Inventory, WarehouseIssueService Desk,
        TestClock Clock, Guid TenantId, Guid EmployeeA, Guid EmployeeB, Guid InactiveEmployee, Guid UserId);

    private static Employee NewEmployee(Guid tenantId, string number, string first, string last, bool active) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = number, FirstName = first, LastName = last,
        DateOfBirth = new(1990, 1, 1), Email = $"{number}@acme.example", PhoneNumber = "+321", Street = "S", HouseNumber = "1",
        PostalCode = "2000", City = "Antwerpen", EmploymentStartDate = new(2020, 1, 1),
        EmploymentStatus = active ? EmploymentStatus.Active : EmploymentStatus.Terminated, IsActive = active,
    };

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "mag@acme.example", FirstName = "Mia", LastName = "Magazijn", IsActive = true });
        var a = NewEmployee(tenantId, "MED-0009", "Anna", "Aerts", true);
        var b = NewEmployee(tenantId, "MED-0010", "Bert", "Bosmans", true);
        var c = NewEmployee(tenantId, "MED-0011", "Carl", "Claes", false);
        db.Context.Employees.AddRange(a, b, c);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var guard = InventoryTestFactory.Guard(currentUser);
        var clock = new TestClock(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, guard);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory,
            new InventoryTestFactory.AllowAllPermissionService(), guard, clock: clock);
        var desk = new WarehouseIssueService(db.Context, tenant, items, clock);
        return new Harness(db, items, inventory, desk, clock, tenantId, a.Id, b.Id, c.Id, userId);
    }

    private static SaveIssuedItemTemplateRequest Template(
        string name = "TEST WH Golden Scanner", bool serial = false, bool returnRequired = false,
        bool stock = true, bool variants = false, bool consumable = false, bool active = true, string? code = null,
        int defaultQuantity = 1) =>
        new(name, "Magazijn", null, defaultQuantity, serial, true, returnRequired, active, 0,
            StockTrackingEnabled: stock, VariantsEnabled: variants, Code: code, IsConsumable: consumable);

    private static WarehouseIssueRequest Issue(Guid employeeId, Guid templateId, int quantity = 1, string? serial = null, Guid? variantId = null) =>
        new(employeeId, templateId, variantId, quantity, serial, new DateOnly(2026, 10, 18));

    private static async Task<IssuedItemTemplateDto> ScannerWithStockAsync(Harness h, int stock = 5)
    {
        var template = await h.Items.CreateTemplateAsync(Template(serial: true, returnRequired: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, stock, null), CancellationToken.None);
        return template;
    }

    private static async Task<int> StockOf(Harness h, Guid templateId) =>
        (await h.Db.Context.IssuedItemTemplates.AsNoTracking().SingleAsync(t => t.Id == templateId)).CurrentStock;

    // ---------------- WH-B1: delete integrity ----------------

    [Fact]
    public async Task B1_ReferencedItem_IsNotDeleted_409WithCounts_AndHistoryStaysReadableAfterDeactivate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-1"), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => h.Items.DeleteTemplateAsync(template.Id, CancellationToken.None));
        Assert.True(refused.CanDeactivate);
        Assert.Contains(refused.Report.Items, i => i.Key == "templateStock");
        Assert.Contains(refused.Report.Items, i => i.Key == "movements");
        Assert.Contains(refused.Report.Items, i => i.Key == "issuedItems");

        // The way out: deactivate. Movements, holders and the employee row all stay readable.
        await h.Items.UpdateTemplateAsync(template.Id, Template(serial: true, returnRequired: true, active: false), CancellationToken.None);
        Assert.Equal(2, (await h.Inventory.ListMovementsAsync(template.Id, CancellationToken.None))!.Count);
        Assert.Single((await h.Inventory.ListCurrentHoldersAsync(template.Id, CancellationToken.None))!);
        Assert.Single((await h.Items.ListForEmployeeAsync(h.EmployeeA, CancellationToken.None))!);
        Assert.Equal(4, await StockOf(h, template.Id));
    }

    [Fact]
    public async Task B1_ClosedIssueHistoryAlone_StillBlocksDelete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: false), CancellationToken.None);
        var item = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id), CancellationToken.None);
        await h.Desk.ReturnAsync(item!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 10, 18)), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => h.Items.DeleteTemplateAsync(template.Id, CancellationToken.None));
        Assert.Contains(refused.Report.Items, i => i.Key == "issuedItems");
    }

    [Fact]
    public async Task B1_UnusedItem_CanBeDeleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(), CancellationToken.None);
        Assert.True(await h.Items.DeleteTemplateAsync(template.Id, CancellationToken.None));
    }

    [Fact]
    public async Task B1_TemplateLevelStockWithoutMovements_StillBlocksDelete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(), CancellationToken.None);
        var entity = await h.Db.Context.IssuedItemTemplates.SingleAsync(t => t.Id == template.Id);
        entity.CurrentStock = 3; // a cached stock never depends on its movements being present
        await h.Db.Context.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => h.Items.DeleteTemplateAsync(template.Id, CancellationToken.None));
        Assert.Contains(refused.Report.Items, i => i is { Key: "templateStock", Count: 3 });
    }

    [Fact]
    public async Task B1_VariantWithHistoryButZeroStock_IsNotDeleted_UnusedVariantIs()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Werkjas", variants: true), CancellationToken.None);
        var used = await h.Inventory.CreateVariantAsync(template.Id, new SaveVariantRequest([], true, 0, 2, Label: "M"), CancellationToken.None);
        var unused = await h.Inventory.CreateVariantAsync(template.Id, new SaveVariantRequest([], true, 1, null, Label: "L"), CancellationToken.None);
        var issued = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, 2, variantId: used!.Id), CancellationToken.None);
        Assert.Equal(0, (await h.Db.Context.IssuedItemVariants.AsNoTracking().SingleAsync(v => v.Id == used.Id)).CurrentStock);

        var refused = await Assert.ThrowsAsync<ReferencedRecordException>(() => h.Inventory.DeleteVariantAsync(template.Id, used.Id, CancellationToken.None));
        Assert.Contains(refused.Report.Items, i => i.Key == "movements");
        Assert.Contains(refused.Report.Items, i => i.Key == "issuedItems");
        Assert.True(await h.Inventory.DeleteVariantAsync(template.Id, unused!.Id, CancellationToken.None));
        Assert.NotNull(issued);
    }

    [Fact]
    public async Task B1_AttributeOptionUsedByVariant_IsNotDeleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Schoenen", variants: true), CancellationToken.None);
        var size = await h.Inventory.CreateAttributeDefinitionAsync(new SaveAttributeDefinitionRequest("Maat", false, true, 0, true), CancellationToken.None);
        var option43 = await h.Inventory.AddAttributeOptionAsync(size.Id, new SaveAttributeOptionRequest("43", 0, true), CancellationToken.None);
        var option44 = await h.Inventory.AddAttributeOptionAsync(size.Id, new SaveAttributeOptionRequest("44", 1, true), CancellationToken.None);
        await h.Inventory.SetTemplateAttributesAsync(template.Id, [size.Id], CancellationToken.None);
        await h.Inventory.CreateVariantAsync(template.Id,
            new SaveVariantRequest([new SaveVariantValueRequest(size.Id, option43!.Id, null)], true, 0, null), CancellationToken.None);

        await Assert.ThrowsAsync<ReferencedRecordException>(() => h.Inventory.DeleteAttributeOptionAsync(size.Id, option43.Id, CancellationToken.None));
        Assert.True(await h.Inventory.DeleteAttributeOptionAsync(size.Id, option44!.Id, CancellationToken.None));
    }

    // ---------------- WH-H1 / M2 / M8: quantity domain ----------------

    [Fact]
    public async Task H1_MassiveReceipt_IsRefused_StockUnchanged_NoMovement()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h, 25);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, int.MaxValue, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, StockQuantities.MaxMovement + 1, null), CancellationToken.None));

        Assert.Equal(25, await StockOf(h, template.Id));
        Assert.Single(await h.Db.Context.StockMovements.Where(m => m.TemplateId == template.Id).ToListAsync());

        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, StockQuantities.MaxMovement, null), CancellationToken.None);
        Assert.Equal(25 + StockQuantities.MaxMovement, await StockOf(h, template.Id));
    }

    [Fact]
    public async Task H1_StockCeiling_IsNeverExceeded_EvenWithValidMovements()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(), CancellationToken.None);
        for (var i = 0; i < StockQuantities.MaxStock / StockQuantities.MaxMovement; i++)
        {
            await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, StockQuantities.MaxMovement, null), CancellationToken.None);
        }

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 1, null), CancellationToken.None));
        Assert.Equal(StockQuantities.MaxStock, await StockOf(h, template.Id));
    }

    [Fact]
    public void H1_LedgerArithmetic_IsCheckedAndBounded()
    {
        Assert.Equal(30, StockQuantities.Apply(25, 5));
        Assert.Throws<DomainValidationException>(() => StockQuantities.Apply(25, int.MaxValue));
        Assert.Throws<DomainValidationException>(() => StockQuantities.Apply(int.MinValue + 10, -100));
        // A wrapped legacy stock can only be corrected back into range.
        Assert.Equal(10, StockQuantities.Apply(-2_147_483_624, 2_147_483_634));
    }

    [Fact]
    public async Task H1_CorrectionAndThresholdBounds_AreValidated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h, 5);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, StockQuantities.MaxStock + 1, "telling", ExpectedCurrentStock: 5), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Inventory.UpdateThresholdsAsync(template.Id,
            new UpdateThresholdsRequest(int.MaxValue, null, null, null, false, true), CancellationToken.None));
        Assert.Equal(5, await StockOf(h, template.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task M2_ZeroOrNegativeIssue_IsRefused_NeverCoercedToOne(int quantity)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Handschoenen"), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 10, null), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, quantity), CancellationToken.None));
        Assert.Contains("quantity", refused.FieldErrors!.Keys);
        Assert.Equal(10, await StockOf(h, template.Id));
        Assert.Empty(await h.Db.Context.EmployeeIssuedItems.ToListAsync());

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.CreateTemplateAsync(Template("Nul", defaultQuantity: 0), CancellationToken.None));
    }

    // ---------------- WH-H2: serial identity ----------------

    [Fact]
    public async Task H2_SameSerial_SecondHolderIsBlocked_NormalizedCaseAndWhitespace()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-GOLD-001"), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeB, template.Id, serial: "  ser-gold -001 "), CancellationToken.None));
        Assert.Contains("Anna Aerts", refused.Message);
        Assert.Contains("serialNumber", refused.FieldErrors!.Keys);

        var holders = await h.Inventory.ListCurrentHoldersAsync(template.Id, CancellationToken.None);
        Assert.Single(holders!);
        Assert.Equal(4, await StockOf(h, template.Id));
    }

    [Fact]
    public async Task H2_ReturnThenReissue_Works_AndKeepsTheCompleteLifecycle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        var toA = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-GOLD-001"), CancellationToken.None);
        await h.Desk.ReturnAsync(toA!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 10, 18)), CancellationToken.None);
        Assert.Equal(5, await StockOf(h, template.Id));

        var toB = await h.Desk.IssueAsync(Issue(h.EmployeeB, template.Id, serial: "SER-GOLD-001"), CancellationToken.None);
        Assert.NotNull(toB);

        var history = await h.Desk.ListIssuesAsync(new InventoryIssueQuery("history", Serial: "ser-gold-001"), CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, r => r.EmployeeName == "Anna Aerts" && r.Status == IssuedItemStatus.Returned);
        Assert.Contains(history, r => r.EmployeeName == "Bert Bosmans" && r.Status == IssuedItemStatus.Issued);

        // Re-opening A's returned row while B holds the serial is a second holder too.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.ReactivateAsync(h.EmployeeA, toA.Id, new ReactivateIssuedItemRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task H2_SerialRequired_WithoutSerial_IsRefused_AndASerialIsOnePiece()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        var missing = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id), CancellationToken.None));
        Assert.Contains("serialNumber", missing.FieldErrors!.Keys);

        var two = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, 2, "SER-9"), CancellationToken.None));
        Assert.Contains("quantity", two.FieldErrors!.Keys);
        Assert.Equal(5, await StockOf(h, template.Id));
    }

    [Fact]
    public async Task H2_DatabaseIndex_RefusesTwoOpenRowsWithTheSameSerial()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        EmployeeIssuedItem Row(Guid employeeId) => new()
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, TemplateId = template.Id,
            NameSnapshot = "Scanner", Status = IssuedItemStatus.Issued, Quantity = 1,
            SerialNumber = "SER-1", NormalizedSerialNumber = "SER-1",
        };
        h.Db.Context.EmployeeIssuedItems.Add(Row(h.EmployeeA));
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.EmployeeIssuedItems.Add(Row(h.EmployeeB));
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.Context.SaveChangesAsync());
    }

    [Fact]
    public void H2_SerialNormalization()
    {
        Assert.Equal("SER-GOLD-001", SerialNumbers.Normalize(" ser-gold-001 "));
        Assert.Equal("SERGOLD001", SerialNumbers.Normalize("ser gold\t001"));
        Assert.Null(SerialNumbers.Normalize("   "));
    }

    // ---------------- WH-H3 / H4: warehouse desk ----------------

    [Fact]
    public async Task H3_WhoHasSerial_AnsweredBySearch_WithoutOpeningEmployees()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-001") with
        {
            IssuedDate = new DateOnly(2026, 10, 1), ExpectedReturnDate = new DateOnly(2026, 10, 10),
        }, CancellationToken.None);
        await h.Desk.IssueAsync(Issue(h.EmployeeB, template.Id, serial: "SER-002"), CancellationToken.None);

        var bySerial = Assert.Single(await h.Desk.ListIssuesAsync(new InventoryIssueQuery(Search: "ser-001"), CancellationToken.None));
        Assert.Equal("MED-0009", bySerial.EmployeeNumber);
        Assert.Equal("BM-0001", bySerial.ItemCode);
        Assert.Equal("Mia Magazijn", bySerial.IssuedByName);
        Assert.True(bySerial.IsOverdue);

        Assert.Equal(2, (await h.Desk.ListIssuesAsync(new InventoryIssueQuery(Search: "golden"), CancellationToken.None)).Count);
        Assert.Single(await h.Desk.ListIssuesAsync(new InventoryIssueQuery(Search: "bosmans"), CancellationToken.None));
        Assert.Single(await h.Desk.ListIssuesAsync(new InventoryIssueQuery(OverdueOnly: true), CancellationToken.None));
    }

    [Fact]
    public async Task H4_IssueToInactiveEmployee_IsRefused_PickerHidesInactiveByDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.InactiveEmployee, template.Id, serial: "SER-1"), CancellationToken.None));

        var options = await h.Desk.SearchEmployeesAsync(null, false, CancellationToken.None);
        Assert.Equal(2, options.Count);
        Assert.DoesNotContain(options, o => o.Id == h.InactiveEmployee);
        Assert.Single(await h.Desk.SearchEmployeesAsync("med-0011", true, CancellationToken.None));
    }

    // ---------------- WH-H5: offboarding ----------------

    [Fact]
    public async Task H5_OutstandingEquipment_IsListedAndCounted_NeverAutoReturned()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var scanner = await ScannerWithStockAsync(h);
        var gloves = await h.Items.CreateTemplateAsync(Template("Handschoenen", consumable: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(gloves.Id, new StockReceiptRequest(null, 20, null), CancellationToken.None);
        await h.Desk.IssueAsync(Issue(h.EmployeeB, scanner.Id, serial: "SER-GOLD-001"), CancellationToken.None);
        await h.Desk.IssueAsync(Issue(h.EmployeeB, gloves.Id, 3), CancellationToken.None);

        var outstanding = await h.Desk.OutstandingForEmployeeAsync(h.EmployeeB, CancellationToken.None);
        Assert.Equal(1, outstanding!.Count);
        Assert.Equal("SER-GOLD-001", outstanding.Items[0].SerialNumber);

        var employee = await h.Db.Context.Employees.SingleAsync(e => e.Id == h.EmployeeB);
        // Integration: Core's authoritative offboarding flow (MD-H6) carries the WH-H5 count.
        var tenant = new DevTenantContext(h.TenantId);
        var offboarding = new EmployeeOffboardingService(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(h.UserId)), h.Clock);
        var summary = await offboarding.OffboardAsync(employee, OffboardingTrigger.ExplicitDeactivation, CancellationToken.None);
        Assert.Equal(1, summary.OutstandingEquipment);
        Assert.False(employee.IsActive);
        Assert.Single(await h.Db.Context.EmployeeIssuedItems.Where(i => i.EmployeeId == h.EmployeeB && i.Status == IssuedItemStatus.Issued).ToListAsync());
    }

    // ---------------- M1: item code ----------------

    [Fact]
    public async Task M1_DuplicateNames_GetDistinctCodes_ExplicitCodeIsUniqueCaseInsensitive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Items.CreateTemplateAsync(Template("Handschoenen"), CancellationToken.None);
        var second = await h.Items.CreateTemplateAsync(Template("Handschoenen"), CancellationToken.None);
        Assert.Equal("BM-0001", first.Code);
        Assert.Equal("BM-0002", second.Code);

        var explicitCode = await h.Items.CreateTemplateAsync(Template("Scanner", code: " scan-01 "), CancellationToken.None);
        Assert.Equal("SCAN-01", explicitCode.Code);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Items.CreateTemplateAsync(Template("Andere", code: "Scan-01"), CancellationToken.None));

        // A deleted item's code is never handed out again.
        var unused = await h.Items.CreateTemplateAsync(Template("Weg"), CancellationToken.None);
        await h.Items.DeleteTemplateAsync(unused.Id, CancellationToken.None);
        var next = await h.Items.CreateTemplateAsync(Template("Nieuw"), CancellationToken.None);
        Assert.Equal("BM-0004", next.Code);
    }

    // ---------------- M3: stale correction ----------------

    [Fact]
    public async Task M3_StaleCorrection_IsRefused_AndTheConcurrentIssueSurvives()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Helm"), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 16, null), CancellationToken.None);

        // Dialog opened at 16; meanwhile an issue takes it to 15.
        await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, 16, "telling", ExpectedCurrentStock: 16), CancellationToken.None));
        Assert.Equal(InventoryService.StockChangedMessage, conflict.Message);
        Assert.Equal(15, await StockOf(h, template.Id));
        Assert.Contains(await h.Db.Context.StockMovements.Where(m => m.TemplateId == template.Id).ToListAsync(),
            m => m.MovementType == StockMovementType.Issue);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, 16, "telling"), CancellationToken.None));

        var fresh = await h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, 14, "telling", ExpectedCurrentStock: 15), CancellationToken.None);
        Assert.Equal(14, fresh!.ResultingStock);
    }

    [Fact]
    public async Task M3_TemplateFormStock_OnStaleValue_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Helm") with { Stock = 16 }, CancellationToken.None);
        await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id), CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => h.Items.UpdateTemplateAsync(template.Id,
            Template("Helm") with { Stock = 20, StockCorrectionReason = "telling", ExpectedCurrentStock = 16 }, CancellationToken.None));
        Assert.Equal(15, await StockOf(h, template.Id));
    }

    // ---------------- M4: history ----------------

    [Fact]
    public async Task M4_History_ShowsActorSerialEmployeeNumberAndDisposition()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        var item = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-7"), CancellationToken.None);
        await h.Desk.ReturnAsync(item!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 10, 18), "scherm gebarsten", "damaged"), CancellationToken.None);

        var movements = (await h.Inventory.ListMovementsAsync(template.Id, CancellationToken.None))!;
        var issue = Assert.Single(movements, m => m.MovementType == StockMovementType.Issue);
        Assert.Equal("Mia Magazijn", issue.PerformedByName);
        Assert.Equal("SER-7", issue.SerialNumber);
        Assert.Equal("MED-0009", issue.EmployeeNumber);
        var damaged = Assert.Single(movements, m => m.MovementType == StockMovementType.Damaged);
        Assert.Equal(0, damaged.Quantity);
        Assert.Equal(item.Id, damaged.IssuedItemId);
    }

    // ---------------- M5: consumables ----------------

    [Fact]
    public async Task M5_Consumable_StockDrops_NoHolderNoLoan_HistoryRecordsConsumption()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var gloves = await h.Items.CreateTemplateAsync(Template("TEST WH Gloves", consumable: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(gloves.Id, new StockReceiptRequest(null, 20, null), CancellationToken.None);

        var item = await h.Desk.IssueAsync(Issue(h.EmployeeA, gloves.Id, 3), CancellationToken.None);
        Assert.Equal(IssuedItemStatus.Consumed, item!.Status);
        Assert.Equal(17, await StockOf(h, gloves.Id));
        Assert.Empty((await h.Inventory.ListCurrentHoldersAsync(gloves.Id, CancellationToken.None))!);
        Assert.Empty(await h.Desk.ListIssuesAsync(new InventoryIssueQuery(), CancellationToken.None));
        Assert.Single(await h.Desk.ListIssuesAsync(new InventoryIssueQuery("history"), CancellationToken.None));
        Assert.Contains((await h.Inventory.ListMovementsAsync(gloves.Id, CancellationToken.None))!,
            m => m.MovementType == StockMovementType.Consumption && m.Quantity == -3 && m.IssuedItemId == item.Id);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.ReturnAsync(item.Id, new ReturnIssuedItemRequest(), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.DeleteItemAsync(h.EmployeeA, item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task M5_ConsumableCannotRequireReturnOrSerial()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.CreateTemplateAsync(Template("X", consumable: true, returnRequired: true), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.CreateTemplateAsync(Template("Y", consumable: true, stock: false), CancellationToken.None));
    }

    // ---------------- M6: archived items ----------------

    [Fact]
    public async Task M6_ArchivedItem_RefusesReceiptAndIssue_ButAcceptsReturnAndCorrection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        var issued = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-1"), CancellationToken.None);
        await h.Items.UpdateTemplateAsync(template.Id, Template(serial: true, returnRequired: true, active: false), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 1, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeB, template.Id, serial: "SER-2"), CancellationToken.None));

        var returned = await h.Desk.ReturnAsync(issued!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 10, 18)), CancellationToken.None);
        Assert.Equal(IssuedItemStatus.Returned, returned!.Status);
        Assert.Equal(5, await StockOf(h, template.Id));
        await h.Inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(null, 0, "afgevoerd", ExpectedCurrentStock: 5), CancellationToken.None);
        Assert.Equal(0, await StockOf(h, template.Id));
    }

    [Fact]
    public async Task M6_InactiveVariant_RefusesReceipt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Werkjas", variants: true), CancellationToken.None);
        var variant = await h.Inventory.CreateVariantAsync(template.Id, new SaveVariantRequest([], false, 0, null, Label: "XL"), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(variant!.Id, 1, null), CancellationToken.None));
    }

    // ---------------- M7: tenant-local dates ----------------

    [Fact]
    public async Task M7_DefaultDates_AreTenantLocal_AfterMidnight()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // 00:30 in Europe/Amsterdam on 19/10 is still 18/10 in UTC.
        h.Clock.Advance(new DateTimeOffset(2026, 10, 18, 22, 30, 0, TimeSpan.Zero) - h.Clock.GetUtcNow());
        var template = await ScannerWithStockAsync(h);
        var issued = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-1") with { IssuedDate = null }, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 10, 19), issued!.IssuedDate);

        var returned = await h.Desk.ReturnAsync(issued.Id, new ReturnIssuedItemRequest(), CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 10, 19), returned!.ReturnedDate);
    }

    // ---------------- Lows ----------------

    [Fact]
    public async Task Low_ReturnBeforeIssue_AndExpectedReturnBeforeIssue_AreRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await ScannerWithStockAsync(h);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-1") with { ExpectedReturnDate = new DateOnly(2026, 10, 1) }, CancellationToken.None));
        var item = await h.Desk.IssueAsync(Issue(h.EmployeeA, template.Id, serial: "SER-1"), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Desk.ReturnAsync(item!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 10, 17)), CancellationToken.None));
    }

    [Fact]
    public async Task Low_VariantNames_AreCaseInsensitive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Werkjas", variants: true), CancellationToken.None);
        await h.Inventory.CreateVariantAsync(template.Id, new SaveVariantRequest([], true, 0, null, Label: "Small"), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.CreateVariantAsync(template.Id, new SaveVariantRequest([], true, 1, null, Label: "small"), CancellationToken.None));
    }
}
