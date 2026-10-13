using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Peppol.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Peppol;

/// <summary>Phase 11: provider seam, sandbox determinism, settings CRUD/audit, verify action, isolation.</summary>
public class PeppolFoundationTests
{
    private static async Task<Guid> SeedTenantAsync(SqliteTestDbContext db, string slug = "t")
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    private static async Task<LegalEntity> SeedLegalEntityAsync(
        SqliteTestDbContext db, Guid tenantId, string name = "Eigen BV",
        string? peppolId = "0123456789", string? peppolScheme = "0208",
        string? vat = "BE0123456749", string? iban = "BE68539007547034")
    {
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = name,
            PeppolId = peppolId, PeppolScheme = peppolScheme, VatNumber = vat, Iban = iban,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.LegalEntities.Add(entity);
        await db.Context.SaveChangesAsync();
        return entity;
    }

    private static async Task<Customer> SeedCustomerAsync(
        SqliteTestDbContext db, Guid tenantId, string name, string? peppolId, string? peppolScheme,
        bool peppolEnabled = false, bool isActive = true)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = $"KL-{Guid.NewGuid().ToString("N")[..6]}",
            Name = name, PeppolId = peppolId, PeppolScheme = peppolScheme,
            PeppolEnabled = peppolEnabled, IsActive = isActive,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Customers.Add(customer);
        await db.Context.SaveChangesAsync();
        return customer;
    }

    /// <summary>Transmissions carry a real FK to invoices; every test transmission needs one.</summary>
    private static async Task<Guid> SeedInvoiceAsync(SqliteTestDbContext db, Guid tenantId)
    {
        // Inactive so these helper customers never skew the overview's active-customer metrics.
        var customer = await SeedCustomerAsync(db, tenantId, $"Factuurklant {Guid.NewGuid():N}", null, null, isActive: false);
        var invoice = new Modules.Invoicing.Entities.Invoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customer.Id,
            InvoiceNumber = $"F-{Guid.NewGuid().ToString("N")[..8]}",
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 7,
            InvoiceDate = new DateOnly(2026, 7, 30), DueDate = new DateOnly(2026, 8, 29),
            Status = Modules.Invoicing.Entities.InvoiceStatus.Sent,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Invoices.Add(invoice);
        await db.Context.SaveChangesAsync();
        return invoice.Id;
    }

    private static PeppolSettingsService SettingsSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new PeppolSettingsService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new PeppolProviderFactory([new SandboxPeppolProvider()]));
    }

    private static PeppolCustomerVerificationService VerificationSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new PeppolCustomerVerificationService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new PeppolProviderFactory([new SandboxPeppolProvider()]),
            TimeProvider.System);
    }

    // --- Sandbox provider determinism ---

    [Fact]
    public async Task Sandbox_ParticipantEndingIn999_IsNotFound()
    {
        var sut = new SandboxPeppolProvider();

        var notFound = await sut.ValidateParticipantAsync("0208", "0123456999", CancellationToken.None);
        var found = await sut.ValidateParticipantAsync("0208", "0123456789", CancellationToken.None);

        Assert.False(notFound.Found);
        Assert.Empty(notFound.SupportedDocumentTypes);
        Assert.True(found.Found);
        Assert.Contains("Invoice", found.SupportedDocumentTypes);
        Assert.Equal("sandbox-0208:0123456789", found.ProviderReference);
    }

    [Fact]
    public async Task Sandbox_FailureMarker_RefusesSubmissionAndValidation()
    {
        var sut = new SandboxPeppolProvider();
        var request = new PeppolOutboundRequest("0208", "1", "0208", "2", "<xml>SANDBOX-FAIL</xml>", "hash", Guid.NewGuid());

        var submission = await sut.SendDocumentAsync(request, CancellationToken.None);
        var validation = await sut.ValidateDocumentAsync("<xml>SANDBOX-FAIL</xml>", CancellationToken.None);

        Assert.False(submission.Accepted);
        Assert.Null(submission.ProviderMessageId);
        Assert.False(validation.Valid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public async Task Sandbox_StatusPolling_AdvancesToDelivered()
    {
        var sut = new SandboxPeppolProvider();
        var request = new PeppolOutboundRequest("0208", "1", "0208", "2", "<xml/>", "hash", Guid.NewGuid());
        var submission = await sut.SendDocumentAsync(request, CancellationToken.None);
        Assert.True(submission.Accepted);
        var messageId = submission.ProviderMessageId!;

        var first = await sut.GetTransmissionStatusAsync(messageId, CancellationToken.None);
        var second = await sut.GetTransmissionStatusAsync(messageId, CancellationToken.None);
        var third = await sut.GetTransmissionStatusAsync(messageId, CancellationToken.None);
        var fourth = await sut.GetTransmissionStatusAsync(messageId, CancellationToken.None);

        Assert.Equal("SubmittedToProvider", first.Status);
        Assert.Equal("AcceptedByProvider", second.Status);
        Assert.Equal("Delivered", third.Status);
        Assert.Equal("Delivered", fourth.Status);
    }

    [Fact]
    public void Factory_UnknownProviderKey_ThrowsDutchValidationError()
    {
        var factory = new PeppolProviderFactory([new SandboxPeppolProvider()]);

        Assert.NotNull(factory.Resolve("sandbox"));
        Assert.NotNull(factory.Resolve("SANDBOX"));
        var ex = Assert.Throws<DomainValidationException>(() => factory.Resolve("acme-ap"));
        Assert.Contains("Onbekende Peppol-provider", ex.Message);
    }

    // --- Settings service ---

    [Fact]
    public async Task GetSettings_WithoutRow_ReturnsDisabledSandboxDefaults()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var entity = await SeedLegalEntityAsync(db, tenantId);
        var sut = SettingsSut(db, tenantId);

        var dto = await sut.GetSettingsAsync(entity.Id, CancellationToken.None);

        Assert.False(dto.Enabled);
        Assert.Equal("Sandbox", dto.Environment);
        Assert.Equal("sandbox", dto.ProviderKey);
        Assert.Equal(entity.LegalName, dto.LegalEntityName);
    }

    [Fact]
    public async Task SaveSettings_CreatesRow_AndAudits()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var entity = await SeedLegalEntityAsync(db, tenantId);
        var sut = SettingsSut(db, tenantId);

        var dto = await sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Sandbox", "sandbox", " facturen@acme.be ", "Bedankt!"),
            CancellationToken.None);

        Assert.True(dto.Enabled);
        Assert.Equal("facturen@acme.be", dto.InvoiceEmailFallback);
        var row = await db.Context.PeppolSettings.SingleAsync(s => s.LegalEntityId == entity.Id);
        Assert.True(row.Enabled);
        Assert.Equal(PeppolEnvironment.Sandbox, row.Environment);
        Assert.True(await db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "PeppolSettings" && a.EntityId == row.Id.ToString() && a.Action == "Created"));

        // Second save is an update on the same row (upsert), audited as Updated.
        await sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(false, "Sandbox", "sandbox", null, null), CancellationToken.None);
        Assert.Equal(1, await db.Context.PeppolSettings.CountAsync(s => s.LegalEntityId == entity.Id));
        Assert.True(await db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "PeppolSettings" && a.EntityId == row.Id.ToString() && a.Action == "Updated"));
    }

    [Fact]
    public async Task SaveSettings_InvalidEnvironmentOrProvider_Throws()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var entity = await SeedLegalEntityAsync(db, tenantId);
        var sut = SettingsSut(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(() => sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Productie", "sandbox", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Sandbox", "acme-ap", null, null), CancellationToken.None));
        // Numeric strings parse to an UNDEFINED enum value; the string-stored column must refuse them.
        await Assert.ThrowsAsync<DomainValidationException>(() => sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "5", "sandbox", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => sut.SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Sandbox", "sandbox", "geen-mailadres", null), CancellationToken.None));
    }

    [Fact]
    public async Task Settings_ForeignLegalEntity_IsInvisible()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a");
        var tenantB = await SeedTenantAsync(db, "b");
        var foreign = await SeedLegalEntityAsync(db, tenantB);
        var sut = SettingsSut(db, tenantA);

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => sut.GetSettingsAsync(foreign.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => sut.SaveSettingsAsync(foreign.Id,
            new SavePeppolSettingsRequest(true, "Sandbox", "sandbox", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Overview_ReportsChecklistCountsAndCustomerGaps()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var complete = await SeedLegalEntityAsync(db, tenantId, "Compleet BV");
        await SeedLegalEntityAsync(db, tenantId, "Incompleet BV", peppolId: null, peppolScheme: null, iban: null);
        // Customer gaps: enabled-without-id counts once; the incomplete active customer counts in the info metric.
        await SeedCustomerAsync(db, tenantId, "Mist ID", peppolId: null, peppolScheme: null, peppolEnabled: true);
        await SeedCustomerAsync(db, tenantId, "In orde", "0123456789", "0208", peppolEnabled: true);
        await SeedCustomerAsync(db, tenantId, "Geen Peppol", null, null);
        db.Context.PeppolTransmissions.AddRange(
            new PeppolTransmission { Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = await SeedInvoiceAsync(db, tenantId), Status = PeppolTransmissionStatus.Queued, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new PeppolTransmission { Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = await SeedInvoiceAsync(db, tenantId), Status = PeppolTransmissionStatus.Delivered, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new PeppolTransmission { Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = await SeedInvoiceAsync(db, tenantId), Status = PeppolTransmissionStatus.Failed, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.PeppolIncomingDocuments.Add(new PeppolIncomingDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SupplierParticipant = "0208:1", DocumentNumber = "F1",
            PayloadHash = new string('a', 64), ProviderMessageId = "in-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        var sut = SettingsSut(db, tenantId);

        var overview = await sut.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(2, overview.LegalEntities.Count);
        var completeRow = overview.LegalEntities.Single(e => e.LegalEntityId == complete.Id);
        Assert.True(completeRow.IsComplete);
        Assert.False(overview.LegalEntities.Single(e => e.LegalEntityName == "Incompleet BV").IsComplete);
        Assert.Equal(1, overview.Counts.Queued);
        Assert.Equal(1, overview.Counts.Delivered);
        Assert.Equal(1, overview.Counts.Failed);
        Assert.Equal(1, overview.Counts.ReceivedIncoming);
        Assert.Equal(1, overview.CustomersEnabledWithoutPeppolId);
        Assert.Equal(2, overview.ActiveCustomersMissingPeppolData);
    }

    [Fact]
    public async Task Overview_IsTenantIsolated()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a");
        var tenantB = await SeedTenantAsync(db, "b");
        await SeedLegalEntityAsync(db, tenantB);
        db.Context.PeppolTransmissions.Add(new PeppolTransmission
        {
            Id = Guid.NewGuid(), TenantId = tenantB, InvoiceId = await SeedInvoiceAsync(db, tenantB),
            Status = PeppolTransmissionStatus.Queued, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        var sut = SettingsSut(db, tenantA);

        var overview = await sut.GetOverviewAsync(CancellationToken.None);

        Assert.Empty(overview.LegalEntities);
        Assert.Equal(0, overview.Counts.Queued);
    }

    [Fact]
    public async Task TestConnection_RequiresOwnPeppolIdentity()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var incomplete = await SeedLegalEntityAsync(db, tenantId, peppolId: null, peppolScheme: null);
        var sut = SettingsSut(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.TestConnectionAsync(incomplete.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TestConnection_ReportsFoundAndNotFound()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var found = await SeedLegalEntityAsync(db, tenantId, "Vindbaar BV");
        var notFound = await SeedLegalEntityAsync(db, tenantId, "Onvindbaar BV", peppolId: "0123456999");
        var sut = SettingsSut(db, tenantId);

        var foundResult = await sut.TestConnectionAsync(found.Id, CancellationToken.None);
        var notFoundResult = await sut.TestConnectionAsync(notFound.Id, CancellationToken.None);

        Assert.True(foundResult.Found);
        Assert.Null(foundResult.Error);
        Assert.False(notFoundResult.Found);
        Assert.NotNull(notFoundResult.Error);
    }

    // --- Customer verify action ---

    [Fact]
    public async Task Verify_StoresFoundOutcomeAndAudits()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var customer = await SeedCustomerAsync(db, tenantId, "Acme", "0123456789", "0208");
        var sut = VerificationSut(db, tenantId);

        var result = await sut.VerifyAsync(customer.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Found);
        var reloaded = await db.Context.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(CustomerPeppolValidationStatus.Found, reloaded.PeppolValidationStatus);
        Assert.NotNull(reloaded.PeppolValidatedAt);
        Assert.Equal("sandbox-0208:0123456789", reloaded.PeppolValidationReference);
        Assert.True(await db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "Customer" && a.EntityId == customer.Id.ToString() && a.Action == "PeppolVerified"));
    }

    [Fact]
    public async Task Verify_StoresNotFoundOutcome()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var customer = await SeedCustomerAsync(db, tenantId, "Acme", "0123456999", "0208");
        var sut = VerificationSut(db, tenantId);

        var result = await sut.VerifyAsync(customer.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Found);
        var reloaded = await db.Context.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(CustomerPeppolValidationStatus.NotFound, reloaded.PeppolValidationStatus);
    }

    [Fact]
    public async Task Verify_WithoutPeppolIdentity_ThrowsDutchValidationError()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var customer = await SeedCustomerAsync(db, tenantId, "Acme", null, null);
        var sut = VerificationSut(db, tenantId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.VerifyAsync(customer.Id, CancellationToken.None));
        Assert.Contains("Peppol-ID", ex.Message);
    }

    [Fact]
    public async Task Verify_ForeignTenantCustomer_ReturnsNull()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a");
        var tenantB = await SeedTenantAsync(db, "b");
        var foreign = await SeedCustomerAsync(db, tenantB, "Acme", "0123456789", "0208");
        var sut = VerificationSut(db, tenantA);

        Assert.Null(await sut.VerifyAsync(foreign.Id, CancellationToken.None));
    }

    // --- One-active-transmission-per-invoice index ---

    [Fact]
    public async Task Transmissions_SecondActiveForSameInvoice_IsRejectedByIndex()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var invoiceId = await SeedInvoiceAsync(db, tenantId);

        PeppolTransmission NewTransmission(PeppolTransmissionStatus status) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceId, Status = status,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

        db.Context.PeppolTransmissions.Add(NewTransmission(PeppolTransmissionStatus.Queued));
        await db.Context.SaveChangesAsync();

        db.Context.PeppolTransmissions.Add(NewTransmission(PeppolTransmissionStatus.Draft));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        db.Context.ChangeTracker.Clear();

        // Once the first is terminal, a fresh transmission for the same invoice is allowed.
        var first = await db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoiceId);
        first.Status = PeppolTransmissionStatus.Failed;
        await db.Context.SaveChangesAsync();
        db.Context.PeppolTransmissions.Add(NewTransmission(PeppolTransmissionStatus.Queued));
        await db.Context.SaveChangesAsync();

        Assert.Equal(2, await db.Context.PeppolTransmissions.CountAsync(t => t.InvoiceId == invoiceId));
    }
}
