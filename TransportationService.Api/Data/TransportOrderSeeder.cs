using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Models;

namespace TransportationService.Api.Data;

public static class TransportOrderSeeder
{
    public static async Task SeedAsync(TransportationDbContext dbContext)
    {
        if (await dbContext.TransportOrders.AnyAsync())
        {
            return;
        }

        dbContext.TransportOrders.AddRange(
            new TransportOrder
            {
                Id = Guid.NewGuid(),
                Reference = "TO-1001",
                CustomerName = "Van Dijk Logistics",
                PickupAddress = "Havenweg 12, Rotterdam",
                DeliveryAddress = "Industrielaan 4, Antwerpen",
                Status = "Gepland"
            },
            new TransportOrder
            {
                Id = Guid.NewGuid(),
                Reference = "TO-1002",
                CustomerName = "Bakkerij De Molen",
                PickupAddress = "Molenstraat 8, Utrecht",
                DeliveryAddress = "Marktplein 3, Amersfoort",
                Status = "Onderweg"
            },
            new TransportOrder
            {
                Id = Guid.NewGuid(),
                Reference = "TO-1003",
                CustomerName = "TechParts B.V.",
                PickupAddress = "Kanaalweg 20, Eindhoven",
                DeliveryAddress = "Bedrijvenpark 15, Tilburg",
                Status = "Afgeleverd"
            });

        await dbContext.SaveChangesAsync();
    }
}
