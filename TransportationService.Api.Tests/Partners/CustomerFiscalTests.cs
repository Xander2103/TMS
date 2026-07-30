using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Common.Validation;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

public class CustomerFiscalTests
{
    private sealed record Harness(SqliteTestDbContext Db, CustomerService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumberPrefix = "KL-" });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        return new Harness(db,
            new CustomerService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new CountryCodeValidator(db.Context)),
            tenantId);
    }

    private static CreateCustomerRequest Request(string name = "Haven BV") => new(
        name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null);

    [Fact]
    public async Task Create_WithBankAndIdentityFields_NormalizesAndPersists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request() with
        {
            Nickname = "Haven",
            CompanyNumber = "0417.497.106",
            CurrencyCode = "eur",
            Iban = "BE68 5390 0754 7034",
            Bic = "kredbebb",
            BankName = "KBC",
        }, CancellationToken.None);

        Assert.Equal("Haven", created.Nickname);
        Assert.Equal("0417.497.106", created.CompanyNumber);
        Assert.Equal("EUR", created.CurrencyCode);
        Assert.Equal("BE68539007547034", created.Iban);
        Assert.Equal("KREDBEBB", created.Bic);
    }

    [Fact]
    public async Task Create_WithInvalidIban_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(Request() with { Iban = "BE00 0000 0000 0000" }, CancellationToken.None));
    }

    [Fact]
    public async Task Create_WithoutFiscalPermission_RejectsFiscalValues_ButAllowsPlainCustomers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var plain = await h.Sut.CreateAsync(Request("Zonder fiscale data"), CancellationToken.None, canManageFiscal: false);
        Assert.Equal("Zonder fiscale data", plain.Name);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(Request("Met VAT") with { VatNumber = "BE0417497106" }, CancellationToken.None, canManageFiscal: false));
    }

    [Fact]
    public async Task Update_WithoutFiscalPermission_BlocksFiscalChange_AllowsRoundTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request() with { VatNumber = "BE0417497106", Iban = "BE68 5390 0754 7034" }, CancellationToken.None);

        UpdateCustomerRequest Update(string? vat, string? iban) => new(
            created.Name, null, vat, null, null, null, null, null, null, null, null, null, null, 30, null, null,
            IsActive: true, Iban: iban);

        // Round-trip of unchanged (normalized) values is fine without the permission.
        var ok = await h.Sut.UpdateAsync(created.Id, Update("BE0417497106", "BE68539007547034"), CancellationToken.None, canManageFiscal: false);
        Assert.NotNull(ok);

        // Changing a fiscal value without the permission is refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.UpdateAsync(created.Id, Update(null, "BE68539007547034"), CancellationToken.None, canManageFiscal: false));
    }

    [Fact]
    public async Task Create_PeppolEnabledWithoutIdentity_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(Request() with { PeppolEnabled = true }, CancellationToken.None));

        var created = await h.Sut.CreateAsync(Request() with
        {
            PeppolEnabled = true, PeppolId = "0417497106", PeppolScheme = "0208",
            PeppolDeliveryPreference = "EmailFallback", BuyerReference = " KP-123 ",
        }, CancellationToken.None);

        Assert.True(created.PeppolEnabled);
        Assert.Equal("EmailFallback", created.PeppolDeliveryPreference);
        Assert.Equal("KP-123", created.BuyerReference);
        Assert.Equal("Unknown", created.PeppolValidationStatus);
    }

    [Fact]
    public async Task Create_InvalidDeliveryPreference_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(Request() with
            {
                PeppolId = "0417497106", PeppolScheme = "0208", PeppolDeliveryPreference = "Duif",
            }, CancellationToken.None));

        // Numeric strings parse to an UNDEFINED enum value; the string-stored column must refuse them.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(Request() with
            {
                PeppolId = "0417497106", PeppolScheme = "0208", PeppolDeliveryPreference = "7",
            }, CancellationToken.None));
    }

    [Fact]
    public async Task Update_ChangedPeppolIdentity_ResetsValidationOutcome()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request() with { PeppolId = "0417497106", PeppolScheme = "0208" }, CancellationToken.None);

        // Simulate an earlier successful provider lookup.
        var entity = await h.Db.Context.Customers.SingleAsync(c => c.Id == created.Id);
        entity.PeppolValidationStatus = Modules.Peppol.Entities.CustomerPeppolValidationStatus.Found;
        entity.PeppolValidatedAt = DateTime.UtcNow;
        entity.PeppolValidationReference = "sandbox-0208:0417497106";
        await h.Db.Context.SaveChangesAsync();

        UpdateCustomerRequest Update(string peppolId) => new(
            created.Name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null,
            IsActive: true, PeppolId: peppolId, PeppolScheme: "0208");

        // Round-trip with the same id keeps the stored outcome.
        await h.Sut.UpdateAsync(created.Id, Update("0417497106"), CancellationToken.None);
        Assert.Equal(Modules.Peppol.Entities.CustomerPeppolValidationStatus.Found,
            (await h.Db.Context.Customers.SingleAsync(c => c.Id == created.Id)).PeppolValidationStatus);

        // A different id invalidates the lookup result.
        var updated = await h.Sut.UpdateAsync(created.Id, Update("0417497107"), CancellationToken.None);
        Assert.Equal("Unknown", updated!.PeppolValidationStatus);
        Assert.Null(updated.PeppolValidatedAt);
        Assert.Null(updated.PeppolValidationReference);
    }

    [Fact]
    public async Task Update_PeppolFieldsAreFiscallyGated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request() with { PeppolId = "0417497106", PeppolScheme = "0208" }, CancellationToken.None);

        UpdateCustomerRequest Update(bool enabled) => new(
            created.Name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null,
            IsActive: true, PeppolId: "0417497106", PeppolScheme: "0208", PeppolEnabled: enabled);

        // Round-trip without changes passes; flipping the Peppol switch without the permission does not.
        Assert.NotNull(await h.Sut.UpdateAsync(created.Id, Update(false), CancellationToken.None, canManageFiscal: false));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.UpdateAsync(created.Id, Update(true), CancellationToken.None, canManageFiscal: false));
    }

    [Fact]
    public void VatTreatmentCatalog_CoversEveryTreatment_WithCoherentRates()
    {
        foreach (var treatment in Enum.GetValues<VatTreatment>())
        {
            var info = VatTreatmentCatalog.Resolve(treatment);
            Assert.Equal(treatment, info.Treatment);
            Assert.False(string.IsNullOrWhiteSpace(info.Label));
        }

        var domestic = VatTreatmentCatalog.Resolve(VatTreatment.DomesticVat);
        Assert.Equal([0m, 6m, 12m, 21m], domestic.StandardRates);
        Assert.True(VatTreatmentCatalog.Resolve(VatTreatment.IntraCommunitySupply).RequiresVatNumber);
        Assert.True(VatTreatmentCatalog.Resolve(VatTreatment.Other).AllowsCustomRate);
    }

    [Fact]
    public void BankingValidators_SharedByEmployeeModule()
    {
        // Employee validators delegate to the shared implementation: same normalisation.
        Assert.Equal(
            BankingValidators.NormalizeIban("BE68 5390 0754 7034"),
            Modules.Employees.Services.EmployeePersonValidators.NormalizeIban("BE68 5390 0754 7034"));
    }

    [Fact]
    public async Task NullRegistryProvider_ReportsNotConfigured()
    {
        var provider = new NullCompanyRegistryProvider();
        Assert.False(provider.IsConfigured);
        Assert.Null(await provider.LookupAsync("BE0417497106", CancellationToken.None));
    }
}
