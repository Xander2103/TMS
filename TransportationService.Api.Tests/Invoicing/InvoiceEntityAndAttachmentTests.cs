using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

public class InvoiceEntityAndAttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Invoices, InvoiceAttachmentService Attachments,
        Guid TenantId, Guid CustomerId, LegalEntity DefaultEntity, LegalEntity CustomerEntity);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, PaymentTermDays = 30 });

        var defaultEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV", TradingName = "Acme",
            VatNumber = "BE0417497106", Iban = "BE68539007547034",
            Street = "Havenlaan", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
            IsActive = true, IsDefault = true,
        };
        var customerEntity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Logistics BV", IsActive = true,
        };
        db.Context.LegalEntities.AddRange(defaultEntity, customerEntity);
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
            DefaultLegalEntityId = customerEntity.Id,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, audit));
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        var attachments = new InvoiceAttachmentService(db.Context, tenant, audit, new LocalFileStorageService(storageRoot));
        return new Harness(db, invoices, attachments, tenantId, customerId, defaultEntity, customerEntity);
    }

    private static CreateInvoiceRequest Request(Harness h, Guid? entityId = null) => new(
        h.CustomerId, null, [], [new ManualInvoiceLineInput("Transport", 1m, 100m, 21m)], null, entityId);

    [Fact]
    public async Task Create_InheritsCustomerDefaultEntity_AndSnapshotsSellerData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);

        Assert.Equal(h.CustomerEntity.Id, result.Invoice!.LegalEntityId);

        var stored = await h.Db.Context.Invoices.SingleAsync(i => i.Id == result.Invoice.Id);
        Assert.Equal("Acme Logistics BV", stored.SellerName);
        Assert.Equal("DomesticVat", stored.CustomerVatTreatment);
    }

    [Fact]
    public async Task Create_ExplicitEntity_WinsOverCustomerDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Invoices.CreateAsync(Request(h, h.DefaultEntity.Id), CancellationToken.None);

        Assert.Equal(h.DefaultEntity.Id, result.Invoice!.LegalEntityId);
        var stored = await h.Db.Context.Invoices.SingleAsync(i => i.Id == result.Invoice.Id);
        Assert.Equal("Acme", stored.SellerName);
        Assert.Equal("Havenlaan 1, 2000 Antwerpen", stored.SellerAddressLine);
    }

    [Fact]
    public async Task Send_WithDeactivatedEntity_IsBlocked_WithExactMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);

        h.CustomerEntity.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var send = await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, send.Outcome);
        Assert.Equal("Deze factuur heeft geen geldige facturerende entiteit en kan niet worden verzonden.", send.Error);
    }

    [Fact]
    public async Task Send_VatNumberRequiredTreatment_WithoutVatNumber_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.VatTreatment = VatTreatment.IntraCommunitySupply;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var created = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);
        var send = await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, send.Outcome);
        Assert.Contains("BTW-nummer", send.Error);
    }

    [Fact]
    public async Task SnapshotIsFrozen_AfterSending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Invoices.CreateAsync(Request(h, h.DefaultEntity.Id), CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        // Later master-data change must not touch the sent invoice.
        h.DefaultEntity.TradingName = "Hernoemd BV";
        await h.Db.Context.SaveChangesAsync();

        var stored = await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == created.Invoice.Id);
        Assert.Equal("Acme", stored.SellerName);
    }

    [Fact]
    public async Task Attachments_UploadListDownloadDelete_Roundtrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);
        var invoiceId = created.Invoice!.Id;

        using var upload = new MemoryStream([1, 2, 3]);
        var attachment = await h.Attachments.UploadAsync(invoiceId, "bon.pdf", "application/pdf", 3, upload, CancellationToken.None);
        Assert.NotNull(attachment);
        Assert.False(attachment!.IncludeWhenSending); // internal by default

        var list = await h.Attachments.ListAsync(invoiceId, CancellationToken.None);
        Assert.Single(list!);

        var toggled = await h.Attachments.UpdateAsync(invoiceId, attachment.Id,
            new UpdateInvoiceAttachmentRequest(true, "meesturen"), CancellationToken.None);
        Assert.True(toggled!.IncludeWhenSending);

        var opened = await h.Attachments.OpenAsync(invoiceId, attachment.Id, CancellationToken.None);
        Assert.NotNull(opened);
        await opened!.Value.Content.DisposeAsync();

        Assert.True(await h.Attachments.DeleteAsync(invoiceId, attachment.Id, CancellationToken.None));
        Assert.Empty((await h.Attachments.ListAsync(invoiceId, CancellationToken.None))!);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.Action == "AttachmentRemoved"));
    }

    [Fact]
    public async Task Attachments_DeleteAfterSending_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);
        using var upload = new MemoryStream([1]);
        var attachment = await h.Attachments.UploadAsync(created.Invoice!.Id, "bon.pdf", "application/pdf", 1, upload, CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Attachments.DeleteAsync(created.Invoice.Id, attachment!.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Attachments_TenantIsolation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Invoices.CreateAsync(Request(h), CancellationToken.None);

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        var foreign = new InvoiceAttachmentService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)),
            new LocalFileStorageService(storageRoot));

        Assert.Null(await foreign.ListAsync(created.Invoice!.Id, CancellationToken.None));
    }
}
