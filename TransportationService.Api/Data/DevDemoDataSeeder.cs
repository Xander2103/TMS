using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Development-only demo master data (fictional): 5 customers with contacts of mixed types
/// and 3–5 locations each (opening hours, gate/dock/instructions, one inactive), plus 10
/// employees (drivers, warehouse, planning, management; multiple notes; one inactive).
/// Triggered explicitly with `dotnet run --project TransportationService.Api -- --seed-demo`
/// and gated behind IsDevelopment in Program.cs. Idempotent: skips entirely when any
/// DEMO-* customer already exists. Writes entities directly (the established seeder
/// pattern) with an explicit TenantId for the "dev" tenant.
/// </summary>
public static class DevDemoDataSeeder
{
    public const string CommandLineFlag = "--seed-demo";
    private const string Marker = "DEMO-";

    public static async Task SeedAsync(TransportationDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        var tenantId = await dbContext.Tenants
            .Where(t => t.Slug == "dev")
            .Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId == Guid.Empty)
        {
            logger.LogWarning("Demo seed skipped: dev tenant not found.");
            return;
        }

        if (await dbContext.Customers.IgnoreQueryFilters()
                .AnyAsync(c => c.TenantId == tenantId && c.CustomerNumber.StartsWith(Marker), cancellationToken))
        {
            logger.LogInformation("Demo seed skipped: DEMO-* data already present.");
            return;
        }

        SeedCustomers(dbContext, tenantId);
        SeedEmployees(dbContext, tenantId);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo seed complete: 5 customers, 10 employees (tenant dev).");
    }

    private static void SeedCustomers(TransportationDbContext db, Guid tenantId)
    {
        var specs = new[]
        {
            (Nr: "DEMO-C001", Name: "Testklant Transport NV", City: "Brussel", Lang: "nl"),
            (Nr: "DEMO-C002", Name: "Haven Logistiek BV", City: "Antwerpen", Lang: "nl"),
            (Nr: "DEMO-C003", Name: "Construct & Co Wegenbouw", City: "Gent", Lang: "nl"),
            (Nr: "DEMO-C004", Name: "Distri-Frais SPRL", City: "Luik", Lang: "fr"),
            (Nr: "DEMO-C005", Name: "Euro Retail Group", City: "Mechelen", Lang: "nl"),
        };

        for (var i = 0; i < specs.Length; i++)
        {
            var (nr, name, city, lang) = specs[i];
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerNumber = nr,
                Name = name,
                Email = $"info@{nr.ToLowerInvariant().Replace("-", "")}.example",
                PhoneNumber = $"+32 2 5{i:00}0 00 00",
                Website = $"https://www.{nr.ToLowerInvariant().Replace("-", "")}.example",
                Street = "Demostraat",
                HouseNumber = $"{10 + i}",
                PostalCode = $"{1000 + i * 500}",
                City = city,
                CountryCode = "BE",
                DefaultLanguageCode = lang,
                Notes = "Fictieve demo-klant voor testinvoer.",
                IsActive = true,
            };
            db.Customers.Add(customer);

            db.CustomerContacts.AddRange(BuildContacts(tenantId, customer.Id, i));

            foreach (var location in BuildLocations(db, tenantId, customer.Id, nr, city, i))
            {
                db.Locations.Add(location);
            }
        }
    }

    private static IEnumerable<CustomerContact> BuildContacts(Guid tenantId, Guid customerId, int index)
    {
        var people = new[]
        {
            (First: "Jan", Last: "Peeters", Type: CustomerContactType.Planning, Primary: true, Role: "Planner"),
            (First: "Sofie", Last: "Janssens", Type: CustomerContactType.Facturatie, Primary: true, Role: "Boekhouding"),
            (First: "Marc", Last: "De Smet", Type: CustomerContactType.Magazijn, Primary: false, Role: "Magazijnier"),
            (First: "Els", Last: "Vermeulen", Type: CustomerContactType.Directie, Primary: true, Role: "Zaakvoerder"),
        };

        // 2–4 contacts per customer, rotating through the pool for variety.
        var count = 2 + index % 3;
        for (var c = 0; c < count; c++)
        {
            var p = people[(index + c) % people.Length];
            yield return new CustomerContact
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                FirstName = p.First,
                LastName = p.Last,
                Role = p.Role,
                ContactType = p.Type,
                IsPrimary = p.Primary,
                IsActive = true,
                Email = $"{p.First.ToLowerInvariant()}.{p.Last.ToLowerInvariant().Replace(" ", "")}@demo.example",
                PhoneNumber = $"+32 47{c} 1{index}0 2{c}0",
            };
        }
    }

    private static IEnumerable<Location> BuildLocations(
        TransportationDbContext db, Guid tenantId, Guid customerId, string customerNr, string city, int index)
    {
        var specs = new[]
        {
            (Suffix: "HQ", Name: $"Hoofdzetel {city}", Type: LocationType.RegisteredOffice, Active: true, Operational: false),
            (Suffix: "MAG", Name: $"Magazijn {city}", Type: LocationType.Warehouse, Active: true, Operational: true),
            (Suffix: "LEV", Name: $"Leveradres {city}", Type: LocationType.UnloadingLocation, Active: true, Operational: false),
            (Suffix: "WERF", Name: $"Werf {city}", Type: LocationType.ConstructionSite, Active: true, Operational: false),
            (Suffix: "OUD", Name: $"Oud depot {city}", Type: LocationType.Depot, Active: false, Operational: false),
        };

        // 3–5 locations per customer; the inactive one is always included for filter testing.
        var count = 3 + index % 3;
        for (var l = 0; l < count; l++)
        {
            var spec = specs[l == count - 1 ? specs.Length - 1 : l];
            var location = new Location
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                Code = $"{customerNr}-{spec.Suffix}",
                Name = spec.Name,
                Type = spec.Type,
                Street = "Nijverheidslaan",
                HouseNumber = $"{l + 1}",
                PostalCode = $"{2000 + index * 100 + l}",
                City = city,
                CountryCode = "BE",
                IsActive = spec.Active,
                ContactName = "Jan Peeters",
                ContactPhone = "+32 475 12 34 56",
            };

            if (spec.Operational)
            {
                location.Gate = "Poort 2";
                location.AccessCode = "1234#";
                location.Dock = "Kade 4";
                location.AppointmentRequired = true;
                location.RouteDescription = "Volg de borden 'Leveranciers' na de hoofdingang.";
                location.LoadingInstructions = "Aanmelden bij de balie; laden enkel via kade 4.";
                location.UnloadingInstructions = "Lossen tussen 07:00 en 16:00; europallets omruilen.";
                location.DriverInstructions = "Veiligheidsvest verplicht op het volledige terrein.";
                location.InternalMemo = "Interne memo: klant wil vaste chauffeur waar mogelijk.";
                location.DefaultLoadingMinutes = 30;
                location.DefaultUnloadingMinutes = 45;
                location.ForkliftAvailable = true;

                foreach (var day in new[] { 1, 2, 3, 4, 5 })
                {
                    db.Set<LocationOpeningInterval>().Add(new LocationOpeningInterval
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        LocationId = location.Id,
                        DayOfWeek = day,
                        FromTime = new TimeOnly(7, 0),
                        ToTime = new TimeOnly(12, 0),
                    });
                    db.Set<LocationOpeningInterval>().Add(new LocationOpeningInterval
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        LocationId = location.Id,
                        DayOfWeek = day,
                        FromTime = new TimeOnly(13, 0),
                        ToTime = new TimeOnly(17, 0),
                    });
                }
            }

            yield return location;
        }
    }

    private static void SeedEmployees(TransportationDbContext db, Guid tenantId)
    {
        var specs = new[]
        {
            (Nr: "DEMO-E001", First: "Rik", Last: "Claes", Driver: true, Active: true, Notes: new[] { "Ervaren chauffeur, kent de haven goed.", "Volgt in oktober een ADR-opfrissing." }),
            (Nr: "DEMO-E002", First: "Anja", Last: "Wouters", Driver: true, Active: true, Notes: new[] { "Voorkeur voor vroege shiften." }),
            (Nr: "DEMO-E003", First: "Tom", Last: "Maes", Driver: true, Active: false, Notes: new[] { "Uit dienst sinds juni." }),
            (Nr: "DEMO-E004", First: "Karim", Last: "El Amrani", Driver: true, Active: true, Notes: new[] { "Nieuwe chauffeur, inwerkperiode loopt." }),
            (Nr: "DEMO-E005", First: "Lies", Last: "De Backer", Driver: false, Active: true, Notes: new[] { "Magazijnverantwoordelijke.", "Heftruckcertificaat vernieuwd." }),
            (Nr: "DEMO-E006", First: "Peter", Last: "Vandenberghe", Driver: false, Active: true, Notes: new[] { "Magazijnmedewerker, ook koerierritten." }),
            (Nr: "DEMO-E007", First: "Sara", Last: "Lemmens", Driver: false, Active: true, Notes: new[] { "Planner; aanspreekpunt voor Testklant Transport NV." }),
            (Nr: "DEMO-E008", First: "Dirk", Last: "Goossens", Driver: false, Active: true, Notes: new[] { "Management; goedkeuring tarieven." }),
            (Nr: "DEMO-E009", First: "Nadia", Last: "Peeters", Driver: false, Active: true, Notes: new[] { "HR en personeelsadministratie." }),
            (Nr: "DEMO-E010", First: "Wim", Last: "Segers", Driver: false, Active: false, Notes: new[] { "Seizoenskracht, contract afgelopen." }),
        };

        var driverSequence = 1;
        foreach (var spec in specs)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeNumber = spec.Nr,
                FirstName = spec.First,
                LastName = spec.Last,
                Email = $"{spec.First.ToLowerInvariant()}.{spec.Last.ToLowerInvariant().Replace(" ", "")}@dev.local",
                PhoneNumber = "+32 470 00 11 22",
                EmploymentStartDate = new DateOnly(2024, 1, 15),
                EmploymentStatus = spec.Active ? EmploymentStatus.Active : EmploymentStatus.Terminated,
                EmploymentEndDate = spec.Active ? null : new DateOnly(2026, 6, 30),
                IsActive = spec.Active,
            };
            db.Employees.Add(employee);

            for (var n = 0; n < spec.Notes.Length; n++)
            {
                db.Set<EmployeeNote>().Add(new EmployeeNote
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    Text = spec.Notes[n],
                    IsPinnedToDashboard = n == 0 && spec.Nr == "DEMO-E001",
                });
            }

            if (spec.Driver)
            {
                db.Set<Modules.Drivers.Entities.Driver>().Add(new Modules.Drivers.Entities.Driver
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    DriverNumber = $"DEMO-D{driverSequence++:000}",
                    IsActive = spec.Active,
                });
            }
        }
    }
}
