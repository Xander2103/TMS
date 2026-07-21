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
