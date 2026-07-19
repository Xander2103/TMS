using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Synchronises the global country reference with <see cref="CountrySeedData"/> on every
/// startup (all environments): inserts missing countries and refreshes name/alpha-3/EU/sort
/// metadata. Never deletes rows and never touches <c>IsActive</c> on existing rows, so a
/// system administrator can retire a country without the seeder resurrecting it.
/// </summary>
public static class CountrySeeder
{
    public static async Task SyncAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Countries.ToDictionaryAsync(c => c.Code, cancellationToken);
        var changed = false;

        foreach (var seed in CountrySeedData.All)
        {
            if (existing.TryGetValue(seed.Code, out var country))
            {
                if (country.Alpha3 != seed.Alpha3 || country.Name != seed.Name
                    || country.IsEuMember != seed.IsEuMember || country.SortOrder != seed.SortOrder)
                {
                    country.Alpha3 = seed.Alpha3;
                    country.Name = seed.Name;
                    country.IsEuMember = seed.IsEuMember;
                    country.SortOrder = seed.SortOrder;
                    changed = true;
                }
            }
            else
            {
                dbContext.Countries.Add(new Country
                {
                    Id = Guid.NewGuid(),
                    Code = seed.Code,
                    Alpha3 = seed.Alpha3,
                    Name = seed.Name,
                    IsEuMember = seed.IsEuMember,
                    IsActive = true,
                    SortOrder = seed.SortOrder,
                });
                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
