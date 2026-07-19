using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Seeds a sensible starter set of tenant-scoped lookups (Belgian/BeNeLux transport context)
/// for every tenant that has none yet. Idempotent per lookup type per tenant.
/// </summary>
public static class ReferenceDataSeeder
{
    public static async Task SeedAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            await SeedForTenantAsync(dbContext, tenantId, cancellationToken);
        }
    }

    private static async Task SeedForTenantAsync(TransportationDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        await SeedIfEmptyAsync<Department>(dbContext, tenantId,
            [("PLAN", "Planning"), ("MAG", "Magazijn"), ("WERK", "Werkplaats"), ("ADMIN", "Administratie"), ("DIR", "Directie")],
            cancellationToken);

        await SeedIfEmptyAsync<JobFunction>(dbContext, tenantId,
            [("CHAUF", "Chauffeur"), ("CHAUF-B", "Chauffeur B"), ("CHAUF-C", "Chauffeur C"), ("CHAUF-CE", "Chauffeur CE"),
             ("PLAN", "Planner"), ("DISP", "Dispatcher"), ("MAGM", "Magazijnmedewerker"),
             ("MONT", "Monteur"), ("ADMM", "Administratief medewerker"), ("KRAAN", "Kraanmachinist"),
             ("FLEET", "Fleetmanager"), ("HR", "HR-medewerker"), ("BOEK", "Boekhouder"), ("DIR", "Directie"),
             ("PREV", "Preventieadviseur")],
            cancellationToken);

        await SeedIfEmptyAsync<VehicleCategory>(dbContext, tenantId,
            [("TREK", "Trekker"), ("BAK", "Bakwagen"), ("BEST", "Bestelwagen"), ("KRAAN", "Kraanwagen")],
            cancellationToken);

        await SeedIfEmptyAsync<TrailerCategory>(dbContext, tenantId,
            [("SCHUIF", "Schuifzeiloplegger"), ("KOEL", "Koeloplegger"), ("TANK", "Tankoplegger"),
             ("PLAT", "Platte oplegger"), ("CONT", "Containerchassis")],
            cancellationToken);

        await SeedIfEmptyAsync<DriverCategory>(dbContext, tenantId,
            [("NAT", "Nationaal"), ("INT", "Internationaal"), ("DISTR", "Distributie"), ("ADR", "ADR"), ("KRAAN", "Kraan")],
            cancellationToken);

        // Business classifications; no "Blocked" category — blocking is a separate customer
        // flag (IsBlocked), keeping one source of truth for that state.
        await SeedIfEmptyAsync<CustomerCategory>(dbContext, tenantId,
            [("STD", "Standaard klant"), ("KEY", "Key account"), ("PROS", "Prospect"),
             ("EENM", "Eenmalige klant"), ("PART", "Partner"), ("OA", "Onderaannemer"),
             ("LEV", "Leverancier"), ("INT", "Interne firma")],
            cancellationToken);

        // Countries are global reference data seeded by CountrySeeder, not tenant lookups.

        await SeedIfEmptyAsync<Language>(dbContext, tenantId,
            [("nl", "Nederlands"), ("fr", "Frans"), ("en", "Engels"), ("de", "Duits")],
            cancellationToken);

        await SeedIfEmptyAsync<Nationality>(dbContext, tenantId,
            [("BE", "Belg"), ("NL", "Nederlander"), ("DE", "Duitser"), ("FR", "Fransman"), ("PL", "Pool")],
            cancellationToken);

        await SeedIfEmptyAsync<ContractType>(dbContext, tenantId,
            [("VAST", "Vast contract"), ("BEP", "Bepaalde duur"), ("UITZ", "Uitzendkracht"), ("ZELF", "Zelfstandig")],
            cancellationToken);
    }

    private static async Task SeedIfEmptyAsync<TEntity>(
        TransportationDbContext dbContext,
        Guid tenantId,
        IReadOnlyList<(string Code, string Name)> items,
        CancellationToken cancellationToken)
        where TEntity : LookupEntity, new()
    {
        var set = dbContext.Set<TEntity>();
        if (await set.AnyAsync(e => e.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        var order = 0;
        foreach (var (code, name) in items)
        {
            set.Add(new TEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                IsActive = true,
                SortOrder = order++,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
