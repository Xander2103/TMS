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
            [("CHAUF", "Chauffeur"), ("PLAN", "Planner"), ("DISP", "Dispatcher"), ("MAGM", "Magazijnmedewerker"),
             ("MONT", "Monteur"), ("ADMM", "Administratief medewerker"), ("KRAAN", "Kraanmachinist")],
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

        await SeedIfEmptyAsync<CustomerCategory>(dbContext, tenantId,
            [("KEY", "Key account"), ("STD", "Standaard"), ("SPOT", "Spot")],
            cancellationToken);

        await SeedIfEmptyAsync<Country>(dbContext, tenantId,
            [("BE", "België"), ("NL", "Nederland"), ("DE", "Duitsland"), ("FR", "Frankrijk"),
             ("LU", "Luxemburg"), ("PL", "Polen")],
            cancellationToken);

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
