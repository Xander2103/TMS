using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reference;

public class CountrySeederTests
{
    [Fact]
    public async Task SyncAsync_SeedsCompleteIsoList()
    {
        using var db = new SqliteTestDbContext();

        await CountrySeeder.SyncAsync(db.Context);

        var count = await db.Context.Countries.CountAsync();
        Assert.Equal(CountrySeedData.All.Count, count);

        var belgium = await db.Context.Countries.SingleAsync(c => c.Code == "BE");
        Assert.Equal("BEL", belgium.Alpha3);
        Assert.True(belgium.IsEuMember);
        Assert.Equal(0, belgium.SortOrder);

        var uk = await db.Context.Countries.SingleAsync(c => c.Code == "GB");
        Assert.False(uk.IsEuMember);
    }

    [Fact]
    public async Task SyncAsync_IsIdempotent_AndReAddsMissingCountries()
    {
        using var db = new SqliteTestDbContext();
        await CountrySeeder.SyncAsync(db.Context);
        var initialCount = await db.Context.Countries.CountAsync();

        // Second run: no duplicates.
        await CountrySeeder.SyncAsync(db.Context);
        Assert.Equal(initialCount, await db.Context.Countries.CountAsync());

        // A removed country is restored on the next sync.
        var nl = await db.Context.Countries.SingleAsync(c => c.Code == "NL");
        db.Context.Countries.Remove(nl);
        await db.Context.SaveChangesAsync();

        await CountrySeeder.SyncAsync(db.Context);
        Assert.NotNull(await db.Context.Countries.SingleOrDefaultAsync(c => c.Code == "NL"));
    }

    [Fact]
    public async Task SyncAsync_DoesNotResurrectDeactivatedCountries()
    {
        using var db = new SqliteTestDbContext();
        await CountrySeeder.SyncAsync(db.Context);

        var russia = await db.Context.Countries.SingleAsync(c => c.Code == "RU");
        russia.IsActive = false;
        await db.Context.SaveChangesAsync();

        await CountrySeeder.SyncAsync(db.Context);

        var reloaded = await db.Context.Countries.SingleAsync(c => c.Code == "RU");
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task SeedData_HasTwentySevenEuMembers_AndUniqueCodes()
    {
        Assert.Equal(27, CountrySeedData.All.Count(c => c.IsEuMember));
        Assert.Equal(CountrySeedData.All.Count, CountrySeedData.All.Select(c => c.Code).Distinct().Count());
        Assert.Equal(CountrySeedData.All.Count, CountrySeedData.All.Select(c => c.Alpha3).Distinct().Count());
        Assert.All(CountrySeedData.All, c =>
        {
            Assert.Equal(2, c.Code.Length);
            Assert.Equal(3, c.Alpha3.Length);
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
        });
    }
}
