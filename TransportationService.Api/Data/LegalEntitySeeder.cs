using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Bootstraps one default <see cref="LegalEntity"/> per tenant from the tenant's
/// CompanySettings profile so every existing installation keeps invoicing without manual
/// setup. Idempotent: tenants that already have a legal entity are skipped entirely.
/// </summary>
public static class LegalEntitySeeder
{
    public static async Task SeedAsync(TransportationDbContext dbContext)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync();
        var tenantsWithEntity = await dbContext.LegalEntities
            .IgnoreQueryFilters()
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync();
        var missing = tenantIds.Except(tenantsWithEntity).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var settingsByTenant = await dbContext.TenantSettings
            .Where(s => missing.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId);
        var tenantNames = await dbContext.Tenants
            .Where(t => missing.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        foreach (var tenantId in missing)
        {
            settingsByTenant.TryGetValue(tenantId, out var settings);
            var legalName = settings?.CompanyLegalName
                ?? settings?.TradingName
                ?? tenantNames.GetValueOrDefault(tenantId)
                ?? "Eigen bedrijf";

            dbContext.LegalEntities.Add(new LegalEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LegalName = legalName,
                TradingName = settings?.TradingName,
                CompanyNumber = settings?.CompanyNumber,
                VatNumber = settings?.VatNumber,
                Street = settings?.Street,
                HouseNumber = settings?.HouseNumber,
                PostalCode = settings?.PostalCode,
                City = settings?.City,
                CountryCode = settings?.CountryCode,
                Email = settings?.Email,
                PhoneNumber = settings?.PhoneNumber,
                Website = settings?.Website,
                Iban = settings?.Iban,
                DefaultCurrency = settings?.DefaultCurrency ?? "EUR",
                PaymentTermDays = settings?.PaymentTermDays ?? 30,
                // Historic numbering keeps its look: continue with the legacy prefix + a
                // month-scoped sequence. Tenants can switch to {YYYY}{MM}{SEQ} in Settings.
                InvoiceNumberFormat = "{PREFIX}{YYYY}{MM}{SEQ}",
                InvoicePrefix = settings?.InvoiceNumberPrefix,
                InvoiceSequencePadding = 4,
                IsActive = true,
                IsDefault = true,
            });
        }

        await dbContext.SaveChangesAsync();
    }
}
