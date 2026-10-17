using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// ACC-M7: the invoice history is a readable story built from the audit rows and the Peppol
/// transmission events — WHEN / WHO / WHAT, Dutch sentences, automatic steps as System.
/// </summary>
public class InvoiceHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceHistoryService Sut, Guid TenantId, Guid UserId, Guid InvoiceId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = T0 });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "boek@acme.be", FirstName = "Bea", LastName = "Boekhouding", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "TEST ACCOUNTING Klant", IsActive = true });
        db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = tenantId, InvoiceNumber = "FAC-2026100006", CustomerId = customerId,
            InvoiceDate = new DateOnly(2026, 10, 17), DueDate = new DateOnly(2026, 11, 16), Status = InvoiceStatus.Sent,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 10,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new InvoiceHistoryService(db.Context, new DevTenantContext(tenantId)), tenantId, userId, invoiceId);
    }

    private static AuditLog Audit(Harness h, string entityType, string entityId, string action, string? oldJson, string? newJson, int minute, Guid? userId) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = userId, EntityType = entityType, EntityId = entityId,
        Action = action, OldValuesJson = oldJson, NewValuesJson = newJson, Timestamp = T0.AddMinutes(minute),
    };

    [Fact]
    public async Task History_TellsTheStory_WithActorsAndDutchTitles()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = h.InvoiceId.ToString();
        var transmission = new PeppolTransmission
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = h.InvoiceId, Status = PeppolTransmissionStatus.Delivered,
            Environment = PeppolEnvironment.Sandbox, ProviderKey = "sandbox", ProviderMessageId = "sbx-123", PayloadVersion = 1,
            SellerParticipant = "0208:1", BuyerParticipant = "0208:2", CreatedAt = T0.AddMinutes(10),
        };
        transmission.Events.AddRange(
            new PeppolTransmissionEvent { Id = Guid.NewGuid(), TenantId = h.TenantId, TransmissionId = transmission.Id, Status = PeppolTransmissionStatus.Queued, Timestamp = T0.AddMinutes(10), Detail = "In wachtrij geplaatst." },
            new PeppolTransmissionEvent { Id = Guid.NewGuid(), TenantId = h.TenantId, TransmissionId = transmission.Id, Status = PeppolTransmissionStatus.SubmittedToProvider, Timestamp = T0.AddMinutes(11), Detail = "Aangeboden aan provider (sandbox)." },
            new PeppolTransmissionEvent { Id = Guid.NewGuid(), TenantId = h.TenantId, TransmissionId = transmission.Id, Status = PeppolTransmissionStatus.Delivered, Timestamp = T0.AddMinutes(12), Detail = "Sandbox poll #3" });
        h.Db.Context.PeppolTransmissions.Add(transmission);
        h.Db.Context.AuditLogs.AddRange(
            Audit(h, "Invoice", id, "Created", null, "{\"InvoiceNumber\":null,\"CustomerId\":\"x\",\"LineCount\":6}", 0, h.UserId),
            Audit(h, "Invoice", id, "Updated", "{\"InvoiceDate\":\"2026-10-17\",\"LineCount\":6}", "{\"InvoiceDate\":\"2026-10-17\",\"LineCount\":7}", 1, h.UserId),
            Audit(h, "Invoice", id, "StatusChanged", "{\"Status\":\"Draft\"}", "{\"Status\":\"Sent\",\"InvoiceNumber\":\"FAC-2026100006\"}", 2, h.UserId),
            Audit(h, "Invoice", id, "Credited", null, "{\"CreditNoteNumber\":\"FAC-CN2026100010\",\"CreditNoteId\":\"y\"}", 20, h.UserId),
            Audit(h, "Invoice", id, "StatusChanged", "{\"Status\":\"Sent\"}", "{\"Status\":\"Paid\"}", 21, h.UserId),
            Audit(h, "PeppolTransmission", transmission.Id.ToString(), "Queued", null, "{\"InvoiceNumber\":\"FAC-2026100006\"}", 10, h.UserId));
        await h.Db.Context.SaveChangesAsync();

        var history = (await h.Sut.GetAsync(h.InvoiceId, CancellationToken.None))!;

        Assert.Equal(
            ["invoice.created", "invoice.updated", "invoice.issued", "peppol.queued", "peppol.submitted", "peppol.delivered", "invoice.credited", "invoice.paid"],
            history.Select(e => e.Type));
        Assert.Equal("Conceptfactuur aangemaakt", history[0].Title);
        Assert.Equal("6 lijnen", history[0].Detail);
        Assert.Equal(("User", "Bea Boekhouding"), (history[0].ActorKind, history[0].ActorName));
        Assert.Equal("Factuurlijnen gewijzigd", history[1].Title);
        Assert.Equal("Factuur definitief gemaakt en verzonden", history[2].Title);
        Assert.Equal("Nummer FAC-2026100006", history[2].Detail);
        // Queueing is the user's action; the dispatcher and the provider are the system.
        Assert.Equal("User", history[3].ActorKind);
        Assert.Contains("sandbox", history[3].Title);
        Assert.Equal(("System", null), (history[4].ActorKind, history[4].ActorName));
        Assert.Equal("Factuur afgeleverd via Peppol", history[5].Title);
        Assert.Contains("sbx-123", history[5].Detail);
        Assert.Equal("Gecrediteerd door creditnota FAC-CN2026100010", history[6].Title);
        Assert.Equal("Gemarkeerd als betaald", history[7].Title);
        Assert.DoesNotContain(history, e => e.Title.Contains('{') || e.Title.Contains("StatusChanged"));
    }

    [Fact]
    public async Task History_OfAnotherTenantsInvoice_IsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var other = new InvoiceHistoryService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));
        Assert.Null(await other.GetAsync(h.InvoiceId, CancellationToken.None));
    }

    [Fact]
    public async Task CreditNote_UsesCreditWording()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var creditId = Guid.NewGuid();
        var customerId = await h.Db.Context.Invoices.Where(i => i.Id == h.InvoiceId).Select(i => i.CustomerId).FirstAsync();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = creditId, TenantId = h.TenantId, InvoiceNumber = "FAC-CN2026100010", CustomerId = customerId, Kind = InvoiceKind.CreditNote,
            CreditedInvoiceId = h.InvoiceId, InvoiceDate = new DateOnly(2026, 10, 17), DueDate = new DateOnly(2026, 11, 16),
            Status = InvoiceStatus.Sent, InvoicePeriodYear = 2026, InvoicePeriodMonth = 10,
        });
        h.Db.Context.AuditLogs.AddRange(
            Audit(h, "Invoice", creditId.ToString(), "CreditNoteCreated", "{\"CreditedInvoiceNumber\":\"FAC-2026100006\"}", "{\"InvoiceNumber\":\"FAC-CN2026100010\",\"LineCount\":6}", 0, h.UserId),
            Audit(h, "Invoice", creditId.ToString(), "StatusChanged", "{\"Status\":\"Draft\"}", "{\"Status\":\"Sent\"}", 1, h.UserId));
        await h.Db.Context.SaveChangesAsync();

        var history = (await h.Sut.GetAsync(creditId, CancellationToken.None))!;
        Assert.Equal("Creditnota FAC-CN2026100010 aangemaakt voor factuur FAC-2026100006", history[0].Title);
        Assert.Equal("Creditnota definitief gemaakt en verzonden", history[1].Title);
    }
}
