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

        await SeedIfEmptyAsync<Modules.Partners.Entities.ContactDepartment>(dbContext, tenantId,
            [("PLAN", "Planning"), ("BOEK", "Boekhouding"), ("AANK", "Aankoop"), ("MAG", "Magazijn"),
             ("DIR", "Directie"), ("KLNT", "Klantendienst"), ("CLAIM", "Claims")],
            cancellationToken);

        await SeedIfEmptyAsync<Modules.Reference.Entities.UnitType>(dbContext, tenantId,
            [("COLLI", "Colli"), ("EUROPALLET", "Europallet"), ("PALLET", "Standaardpallet"), ("BLOCKPALLET", "Blokpallet"),
             ("CONTAINER", "Container"), ("CRATE", "Krat"), ("BOX", "Doos"), ("ROLLCONTAINER", "Rolcontainer"),
             ("PIECE", "Stuks"), ("LOADINGMETER", "Laadmeter"), ("KG", "Kilogram"), ("TON", "Ton"),
             ("DRUM", "Vat"), ("DOCUMENT", "Document"), ("PARCEL", "Pakket"), ("OTHER", "Andere")],
            cancellationToken);

        await SeedIfEmptyAsync<Modules.Employees.Entities.IssuedItemCategory>(dbContext, tenantId,
            [("KLEDING", "Kleding"), ("SCHOENEN", "Schoenen"), ("IT", "IT"), ("VEILIGHEID", "Veiligheidsmateriaal"),
             ("TOEGANG", "Toegangsmiddelen"), ("GEREEDSCHAP", "Gereedschap"), ("OVERIG", "Overig")],
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

        // Languages and nationalities converge on the full standard list (add-if-missing by
        // code, mirroring the CountrySeeder idea) so existing tenants also get the long tail;
        // entries a tenant renamed or deactivated are never touched.
        await SeedMissingAsync<Language>(dbContext, tenantId, NationalityLanguageSeedData.Languages, cancellationToken);
        await SeedMissingAsync<Nationality>(dbContext, tenantId, NationalityLanguageSeedData.Nationalities, cancellationToken);

        await SeedIfEmptyAsync<ContractType>(dbContext, tenantId,
            [("VAST", "Vast contract"), ("BEP", "Bepaalde duur"), ("UITZ", "Uitzendkracht"), ("ZELF", "Zelfstandig")],
            cancellationToken);

        await SeedServiceOptionsAsync(dbContext, tenantId, cancellationToken);
    }

    /// <summary>Starter delivery services/supplements; prices are tenant-editable placeholders.</summary>
    private static async Task SeedServiceOptionsAsync(
        TransportationDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        if (await dbContext.ServiceOptions.AnyAsync(o => o.TenantId == tenantId, cancellationToken))
        {
            return;
        }

        (string Code, string Name, decimal Value)[] options =
        [
            ("VOOR8", "Levering vóór 08:00", 25m),
            ("VOOR10", "Levering vóór 10:00", 15m),
            ("LAADKLEP", "Laadklep", 10m),
            ("KRAAN", "Kraanlossing", 75m),
            ("ADR", "ADR-transport", 50m),
            ("WACHTTIJD", "Wachttijd (per uur)", 45m),
            ("EXTRASTOP", "Extra stop", 20m),
            ("ZATERDAG", "Zaterdaglevering", 40m),
        ];
        var order = 0;
        foreach (var (code, name, value) in options)
        {
            dbContext.ServiceOptions.Add(new Modules.Tarification.Entities.ServiceOption
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                Kind = Modules.Tarification.Entities.SurchargeKind.Fixed,
                DefaultValue = value,
                IsActive = true,
                SortOrder = order++,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedMissingAsync<TEntity>(
        TransportationDbContext dbContext,
        Guid tenantId,
        IReadOnlyList<(string Code, string Name, int SortOrder)> items,
        CancellationToken cancellationToken)
        where TEntity : LookupEntity, new()
    {
        var set = dbContext.Set<TEntity>();
        var existing = (await set
                .Where(e => e.TenantId == tenantId)
                .Select(e => e.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var (code, name, sortOrder) in items)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            set.Add(new TEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                IsActive = true,
                SortOrder = sortOrder,
            });
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
