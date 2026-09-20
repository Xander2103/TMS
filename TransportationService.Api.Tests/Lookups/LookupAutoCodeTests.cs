using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Lookups;

/// <summary>HR wave 2026-09-12 §4: server-side unique code generation for departments / contract types / job functions.</summary>
public class LookupAutoCodeTests
{
    private static LookupService<TEntity> CreateSut<TEntity>(TransportationDbContextHolder holder) where TEntity : LookupEntity, new()
    {
        var tenantContext = new DevTenantContext(holder.TenantId);
        var audit = new AuditService(holder.Db.Context, tenantContext, new DevCurrentUserContext(null));
        return new LookupService<TEntity>(holder.Db.Context, tenantContext, audit);
    }

    private sealed record TransportationDbContextHolder(SqliteTestDbContext Db, Guid TenantId);

    private static async Task<TransportationDbContextHolder> SeedAsync(params IInterceptor[] interceptors)
    {
        var db = new SqliteTestDbContext(null, interceptors);
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = $"t-{tenantId:N}", CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return new TransportationDbContextHolder(db, tenantId);
    }

    private static CreateLookupRequest Request(string? code, string name) => new(code, name, null, true, 0);

    // ---- pure generator ----

    [Theory]
    [InlineData("Planning", "PLANNING")]
    [InlineData("Magazijn & logistiek", "MAGAZIJNLO")]
    [InlineData("Préventie", "PREVENTIE")]
    [InlineData("  ", "CODE")]
    [InlineData("***", "CODE")]
    public void BaseFromName_IsUppercaseAlphanumeric_MaxTenChars(string name, string expected)
    {
        Assert.Equal(expected, LookupCodeGenerator.BaseFromName(name));
    }

    [Fact]
    public void NextFree_SkipsTakenCodes_CaseInsensitive()
    {
        Assert.Equal("PLAN", LookupCodeGenerator.NextFree("PLAN", []));
        Assert.Equal("PLAN-2", LookupCodeGenerator.NextFree("PLAN", ["plan"]));
        Assert.Equal("PLAN-4", LookupCodeGenerator.NextFree("PLAN", ["PLAN", "PLAN-2", "plan-3"]));
    }

    // ---- service ----

    [Fact]
    public async Task ExplicitUniqueCode_IsStoredAsGiven()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<Department>(h);

        var result = await sut.CreateAsync(Request("PLAN", "Planning"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.Success, result.Status);
        Assert.Equal("PLAN", result.Item!.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyCode_GeneratesNameDerivedCode(string? code)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<JobFunction>(h);

        var result = await sut.CreateAsync(Request(code, "Kraanmachinist"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.Success, result.Status);
        Assert.Equal("KRAANMACHI", result.Item!.Code);
        Assert.Equal("Kraanmachinist", result.Item.Name);
    }

    [Fact]
    public async Task TwoSuccessiveCreates_WithSameName_GetDistinctCodes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<ContractType>(h);

        var first = await sut.CreateAsync(Request(null, "Vast"), CancellationToken.None);
        var second = await sut.CreateAsync(Request(null, "Vast"), CancellationToken.None);
        var third = await sut.CreateAsync(Request(null, "vast"), CancellationToken.None);

        Assert.Equal("VAST", first.Item!.Code);
        Assert.Equal("VAST-2", second.Item!.Code);
        Assert.Equal("VAST-3", third.Item!.Code);
        Assert.Equal(3, await h.Db.Context.ContractTypes.CountAsync());
    }

    [Fact]
    public async Task DuplicateExplicitCode_ReturnsClearValidationOutcome_AndDoesNotAutoRename()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<Department>(h);
        await sut.CreateAsync(Request("MAG", "Magazijn"), CancellationToken.None);

        var duplicate = await sut.CreateAsync(Request("mag", "Magazijn 2"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.DuplicateCode, duplicate.Status);
        Assert.Contains("mag", duplicate.Error);
        Assert.Equal(1, await h.Db.Context.Departments.CountAsync());
    }

    [Fact]
    public async Task ExistingCodes_AreNeverRewritten_ByGeneration()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<Department>(h);
        await sut.CreateAsync(Request("PLAN", "Planning"), CancellationToken.None);
        await sut.CreateAsync(Request("PLAN-2", "Planning nacht"), CancellationToken.None);

        var generated = await sut.CreateAsync(Request(null, "Plan"), CancellationToken.None);

        Assert.Equal("PLAN-3", generated.Item!.Code);
        var codes = await h.Db.Context.Departments.OrderBy(d => d.Code).Select(d => d.Code).ToListAsync();
        Assert.Equal(["PLAN", "PLAN-2", "PLAN-3"], codes);
    }

    [Fact]
    public async Task GeneratedCode_SkipsSoftDeletedCodes_SoHistoryIsNotResurrected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<Department>(h);
        var old = await sut.CreateAsync(Request(null, "Directie"), CancellationToken.None);
        Assert.True(await sut.DeleteAsync(old.Item!.Id, CancellationToken.None));

        var again = await sut.CreateAsync(Request(null, "Directie"), CancellationToken.None);

        Assert.Equal("DIRECTIE-2", again.Item!.Code);
    }

    [Fact]
    public async Task ValidationFailure_WhenNameMissing_EvenWithoutCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = CreateSut<Department>(h);

        var result = await sut.CreateAsync(Request(null, " "), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.ValidationFailed, result.Status);
    }

    /// <summary>
    /// Race: a concurrent writer inserts the same generated candidate between the uniqueness
    /// check and SaveChanges. The unique index rejects the loser, which must transparently take
    /// the next free ordinal instead of failing.
    /// </summary>
    [Fact]
    public async Task ConcurrentCreate_LoserFallsBackToNextFreeCode()
    {
        var competitor = new CompetingInsertInterceptor();
        var h = await SeedAsync(competitor);
        using var _ = h.Db;
        competitor.Configure(h.Db, h.TenantId, "PLANNING");
        var sut = CreateSut<Department>(h);

        var result = await sut.CreateAsync(Request(null, "Planning"), CancellationToken.None);

        Assert.Equal(LookupOperationStatus.Success, result.Status);
        Assert.Equal("PLANNING-2", result.Item!.Code);
        var codes = await h.Db.Context.Departments.OrderBy(d => d.Code).Select(d => d.Code).ToListAsync();
        Assert.Equal(["PLANNING", "PLANNING-2"], codes);
        Assert.Equal(1, competitor.Inserts);
    }

    /// <summary>Simulates another request winning the race: once, right before the first insert of a
    /// department commits, it writes a row with the same code through a second context.</summary>
    private sealed class CompetingInsertInterceptor : SaveChangesInterceptor
    {
        private SqliteTestDbContext? _db;
        private Guid _tenantId;
        private string _code = string.Empty;
        public int Inserts { get; private set; }

        public void Configure(SqliteTestDbContext db, Guid tenantId, string code)
        {
            _db = db;
            _tenantId = tenantId;
            _code = code;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_db is not null && Inserts == 0 && eventData.Context is { } context
                && context.ChangeTracker.Entries<Department>().Any(e => e.State == EntityState.Added && e.Entity.Code == _code))
            {
                Inserts++;
                await using var other = _db.CreateContextForTenant(_tenantId);
                other.Departments.Add(new Department { Id = Guid.NewGuid(), TenantId = _tenantId, Code = _code, Name = "Concurrent", IsActive = true });
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
