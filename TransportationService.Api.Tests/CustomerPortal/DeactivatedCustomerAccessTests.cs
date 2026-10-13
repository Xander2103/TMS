using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// H-14, fix round 1: deactivating a customer must cut portal access through EVERY
/// `/api/customer-portal/*` entry point, not just the ones that happened to remember the rule.
/// Each portal service resolves the caller's customer itself; this suite drives all seven of
/// those resolvers against ONE seed so a newly added service that forgets the `IsActive` join
/// cannot slip through — the most dangerous of them (`CustomerPortalUserService`) can otherwise
/// mint working credentials for a customer the tenant has just switched off.
/// </summary>
public class DeactivatedCustomerAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TestClock Clock, Guid TenantId, Guid CustomerId,
        Guid PortalUserId, Guid SecondPortalUserId, Guid OrderId, Guid DocumentId, string StorageRoot)
    {
        private DevTenantContext Tenant => new(TenantId);
        private DevCurrentUserContext Caller => new(PortalUserId);
        private AuditService Audit => new(Db.Context, Tenant, Caller);
        private LocalFileStorageService Storage => new(StorageRoot);

        public CustomerPortalService Orders()
        {
            var tenant = Tenant;
            var audit = Audit;
            return new CustomerPortalService(Db.Context, tenant, Caller,
                new TransportOrderService(Db.Context, tenant, audit, Clock),
                new LocationService(Db.Context, tenant, audit, new CountryCodeValidator(Db.Context)), audit);
        }

        public PortalDocumentService Documents() =>
            new(Db.Context, Tenant, Caller, Storage);

        public PortalInvoiceService Invoices()
        {
            var tenant = Tenant;
            var audit = Audit;
            var storage = Storage;
            var invoices = new InvoiceService(Db.Context, tenant, audit, Clock,
                new InvoiceNumberService(Db.Context, tenant),
                new CustomerBillingConfigService(Db.Context, tenant, audit, Clock),
                new AccountingService(Db.Context, tenant, audit));
            return new PortalInvoiceService(Db.Context, tenant, Caller, invoices,
                new InvoicePdfService(Db.Context, tenant, storage),
                new InvoiceAttachmentService(Db.Context, tenant, audit, storage));
        }

        public PortalDashboardService Dashboard()
        {
            var tenant = Tenant;
            var audit = Audit;
            var invoices = new InvoiceService(Db.Context, tenant, audit, Clock,
                new InvoiceNumberService(Db.Context, tenant),
                new CustomerBillingConfigService(Db.Context, tenant, audit, Clock),
                new AccountingService(Db.Context, tenant, audit));
            return new PortalDashboardService(Db.Context, tenant, Caller, Messages(), invoices,
                new PortalAnnouncementService(Db.Context, tenant, Caller, audit, Clock), Clock);
        }

        public CustomerMessageService Messages() =>
            new(Db.Context, Tenant, Caller, Audit, Clock);

        public PortalAnnouncementService Announcements() =>
            new(Db.Context, Tenant, Caller, Audit, Clock);

        public PortalMessageService PortalMessages() =>
            new(Db.Context, Tenant, Caller, Audit, Clock,
                new PermissionAuthorizationService(Db.Context), new MessageOutboxService(Db.Context, Tenant, Clock));

        public CustomerPortalUserService Users()
        {
            var tenant = Tenant;
            var caller = Caller;
            var audit = Audit;
            return new CustomerPortalUserService(Db.Context, tenant, caller,
                new UserAccountFlowService(Db.Context, tenant, new PasswordHasher(), audit, Clock, new TestHostEnvironment()),
                new MessageOutboxService(Db.Context, tenant, Clock),
                new DevelopmentSinkProvider(Path.Combine(StorageRoot, "mail")),
                audit, Clock, new ConfigurationBuilder().Build());
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var secondPortalUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-deactivated-customer", Guid.NewGuid().ToString("N"));

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        db.Context.Users.AddRange(
            new User
            {
                Id = portalUserId, TenantId = tenantId, Email = "beheer@haven.be", FirstName = "Bea", LastName = "Heer",
                CustomerId = customerId, IsActive = true,
            },
            new User
            {
                Id = secondPortalUserId, TenantId = tenantId, Email = "collega@haven.be", FirstName = "Cis", LastName = "Collega",
                CustomerId = customerId, IsActive = true,
            });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new DateOnly(2026, 8, 30), Status = TransportOrderStatus.Completed,
        });

        var fileStorage = new LocalFileStorageService(storageRoot);
        using (var content = new MemoryStream("cmr"u8.ToArray()))
        {
            var path = await fileStorage.SaveAsync(tenantId, "order-documents", "cmr.pdf", content, CancellationToken.None);
            db.Context.TransportOrderDocuments.Add(new TransportOrderDocument
            {
                Id = documentId, TenantId = tenantId, TransportOrderId = orderId, Title = "CMR",
                DocumentType = TransportOrderDocumentType.Cmr, DocumentPath = path, FileName = "cmr.pdf",
                ContentType = "application/pdf", CustomerVisible = true,
            });
        }

        db.Context.PortalAnnouncements.Add(new TransportationService.Api.Modules.CustomerPortal.Entities.PortalAnnouncement
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Title = "Kerstsluiting",
            Body = "Wij zijn gesloten van 24/12 tot 02/01.", IsActive = true,
        });

        db.Context.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, InvoiceNumber = "2026080001",
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 8, InvoiceDate = new DateOnly(2026, 8, 30),
            DueDate = new DateOnly(2026, 9, 29), Status = InvoiceStatus.Sent, Currency = "EUR",
        });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        await PermissionCatalogSeeder.SyncAsync(db.Context);
        await DefaultRoleSeeder.SyncAsync(db.Context);

        return new Harness(db, new TestClock(Now), tenantId, customerId, portalUserId, secondPortalUserId,
            orderId, documentId, storageRoot);
    }

    private static async Task DeactivateCustomerAsync(Harness h)
    {
        var customer = h.Db.Context.Customers.Single(c => c.Id == h.CustomerId);
        customer.IsActive = false;
        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>Sanity check: everything the "after" test asserts is refused, works while the
    /// customer is active — otherwise the refusals below would prove nothing.</summary>
    [Fact]
    public async Task WhileTheCustomerIsActive_EveryPortalEntryPointWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(PortalOutcomeKind.Success, (await h.Orders().GetContextAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Documents().ListMyDocumentsAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success,
            (await h.Documents().GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.DocumentId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Invoices().ListMyInvoicesAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Dashboard().GetDashboardAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Messages().ListPortalAsync(null, CancellationToken.None)).Outcome);
        Assert.NotNull(await h.PortalMessages().ListFeedAsync(CancellationToken.None));
        Assert.Equal(PortalOutcomeKind.Success, (await h.Users().ListAsync(CancellationToken.None)).Outcome);
        var announcements = await h.Announcements().ListForPortalAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, announcements.Outcome);
        Assert.Single(announcements.Value!);
        Assert.Equal(PortalUserOperationOutcome.Success,
            (await h.Users().DeactivateAsync(h.SecondPortalUserId, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task DeactivatedCustomer_LosesOrderContextListDetailAndLocations()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);
        var sut = h.Orders();

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.GetContextAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.ListMyOrdersAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.GetMyOrderAsync(h.OrderId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.ListMyLocationsAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink,
            (await sut.GetNotificationPreferencesAsync(CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task DeactivatedCustomer_LosesDocumentsInvoicesAndDashboard()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await h.Documents().ListMyDocumentsAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink,
            (await h.Documents().GetDocumentContentAsync(PortalDocumentSource.OrderDocument, h.DocumentId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await h.Invoices().ListMyInvoicesAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await h.Dashboard().GetDashboardAsync(CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task DeactivatedCustomer_LosesTheOrderMessageThread()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);
        var sut = h.Messages();

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.ListPortalAsync(null, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink,
            (await sut.SendPortalAsync(new SendCustomerMessageRequest(h.OrderId, "Waar blijft mijn levering?"), CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.GetPortalUnreadCountAsync(CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.MarkPortalReadAsync(null, CancellationToken.None)).Outcome);
        Assert.Empty(h.Db.Context.CustomerMessages.ToList());
    }

    [Fact]
    public async Task DeactivatedCustomer_LosesThePortalMessageInbox()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);
        var sut = h.PortalMessages();

        Assert.Null(await sut.ListFeedAsync(CancellationToken.None));
        Assert.Null(await sut.FeedUnreadCountAsync(CancellationToken.None));
    }

    /// <summary>
    /// Fix wave B, item B3 (pass-2 finding I-4): the announcements endpoint was the ONE portal
    /// route that never ran a customer resolver, so a deactivated customer kept receiving the
    /// tenant's broadcast notices. Announcements have no per-customer targeting today, which is
    /// why the impact was low — but "every portal endpoint resolves the caller's active customer
    /// first" is the rule, and an exception to it silently widens the day targeting lands.
    /// </summary>
    [Fact]
    public async Task DeactivatedCustomer_LosesTheAnnouncementFeed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);

        var result = await h.Announcements().ListForPortalAsync(CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, result.Outcome);
        Assert.Null(result.Value);
        // The admin-side listing is unchanged — it is gated by an internal permission, not by the
        // portal resolver, and must keep working for staff of a deactivated customer's tenant.
        Assert.Single(await h.Announcements().ListAllAsync(CancellationToken.None));
    }

    /// <summary>
    /// The sharpest edge: portal user management. A deactivated customer's administrator may not
    /// list, invite, (de)activate or re-invite anyone — inviting would hand out fresh working
    /// credentials for a customer the tenant just switched off.
    /// </summary>
    [Fact]
    public async Task DeactivatedCustomer_CannotManageOrMintPortalUsers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await DeactivateCustomerAsync(h);
        var sut = h.Users();

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, (await sut.ListAsync(CancellationToken.None)).Outcome);

        var invited = await sut.InviteAsync(
            new PortalInviteUserRequest("Nieuwe", "Klant", "nieuw@haven.be", new PortalUserGrantsDto(false, false, false)),
            CancellationToken.None);
        Assert.Equal(PortalUserOperationOutcome.ValidationFailed, invited.Outcome);
        Assert.DoesNotContain(h.Db.Context.Users.ToList(), u => u.Email == "nieuw@haven.be");

        Assert.Equal(PortalUserOperationOutcome.NotFound,
            (await sut.DeactivateAsync(h.SecondPortalUserId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalUserOperationOutcome.NotFound,
            (await sut.ReactivateAsync(h.SecondPortalUserId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalUserOperationOutcome.NotFound,
            (await sut.ResendInviteAsync(h.SecondPortalUserId, CancellationToken.None)).Outcome);
        Assert.Equal(PortalUserOperationOutcome.NotFound,
            (await sut.SetGrantsAsync(h.SecondPortalUserId, new PortalUserGrantsDto(true, true, true), CancellationToken.None)).Outcome);

        // Nothing was written: the second user keeps its state and no activation token was issued.
        Assert.True(h.Db.Context.Users.Single(u => u.Id == h.SecondPortalUserId).IsActive);
    }
}
