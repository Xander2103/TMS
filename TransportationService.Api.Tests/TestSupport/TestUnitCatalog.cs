using TransportationService.Api.Data;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// MD-H8: orders only accept unit codes of the tenant's unit master, so harnesses that create
/// orders with unit codes seed the standard transport units first (as ReferenceDataSeeder does).
/// </summary>
public static class TestUnitCatalog
{
    public static readonly (string Code, string Name)[] Standard =
    [
        ("EUROPALLET", "Europallet"), ("PALLET", "Pallet"), ("BLOCKPALLET", "Blokpallet"), ("COLLI", "Colli"),
        ("PARCEL", "Pakket"), ("BOX", "Doos"), ("CRATE", "Krat"), ("ROLLCONTAINER", "Rolcontainer"),
        ("DRUM", "Vat"), ("CONTAINER", "Container"), ("DOCUMENT", "Document"), ("OTHER", "Anders"), ("PIECE", "Stuk"),
    ];

    public static void Seed(TransportationDbContext context, Guid tenantId)
    {
        var sort = 0;
        foreach (var (code, name) in Standard)
        {
            context.UnitTypes.Add(new UnitType { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = name, IsActive = true, SortOrder = sort++ });
        }
    }
}
