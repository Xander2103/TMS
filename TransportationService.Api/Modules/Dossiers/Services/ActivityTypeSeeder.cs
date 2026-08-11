using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IActivityTypeSeeder
{
    Task EnsureSeededAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lazy per-tenant seed of the default activity catalogue (AccountingService pattern):
/// add-if-missing on Code, never resurrect a deliberately deleted row, never overwrite
/// tenant edits. This is the ONLY place in the backend that knows these codes — domain
/// logic drives on the capability flags, so another tenant can replace the whole set
/// (container, reefer, intermodal, ...) through the settings UI without source changes.
/// </summary>
public class ActivityTypeSeeder : IActivityTypeSeeder
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;

    public ActivityTypeSeeder(TransportationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private sealed record Seed(
        string Code, string Name, string Icon, int SortOrder, string? KpiCategory,
        bool HasStops, bool SupportsGoods, bool PlanningRelevant, bool WarehouseRelevant,
        bool AllowsDuration, bool IsQuickStart, int QuickStartOrder, bool IsDefaultTransport);

    private static readonly Seed[] Defaults =
    [
        new("DISTRIBUTIE", "Distributie", "route", 0, "Distributie",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: true,
            AllowsDuration: false, IsQuickStart: true, QuickStartOrder: 1, IsDefaultTransport: false),
        new("DIRECT_TRANSPORT", "Direct transport", "truck", 1, "Transport",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: false, IsQuickStart: true, QuickStartOrder: 2, IsDefaultTransport: true),
        new("KRAANTRANSPORT", "Kraantransport", "crane", 2, "Kraan",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: true, IsQuickStart: true, QuickStartOrder: 3, IsDefaultTransport: false),
        new("KRAANWERK", "Kraanwerk ter plaatse", "crane", 3, "Kraan",
            HasStops: false, SupportsGoods: false, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: true, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
        new("PLATEAU", "Plateau", "trailer", 4, "Kraan",
            HasStops: false, SupportsGoods: false, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: true, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
        new("OPSLAG", "Opslag", "warehouse", 5, "Opslag",
            HasStops: false, SupportsGoods: true, PlanningRelevant: false, WarehouseRelevant: true,
            AllowsDuration: false, IsQuickStart: true, QuickStartOrder: 4, IsDefaultTransport: false),
        new("EXPRESS", "Express", "zap", 6, "Transport",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: false, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
        new("HERLEVERING", "Herlevering", "rotate", 7, "Distributie",
            HasStops: true, SupportsGoods: true, PlanningRelevant: true, WarehouseRelevant: true,
            AllowsDuration: false, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
        new("POSITIONERING", "Positionering / lege rit", "move", 8, "Transport",
            HasStops: true, SupportsGoods: false, PlanningRelevant: true, WarehouseRelevant: false,
            AllowsDuration: false, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
        new("OVERIG", "Overig", "more", 9, null,
            HasStops: false, SupportsGoods: false, PlanningRelevant: false, WarehouseRelevant: false,
            AllowsDuration: true, IsQuickStart: false, QuickStartOrder: 0, IsDefaultTransport: false),
    ];

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;

        // IgnoreQueryFilters: a deliberately deleted default type is never resurrected.
        var existingCodes = await _db.ActivityTypes.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId).Select(t => t.Code).ToListAsync(cancellationToken);

        // The default-transport flag is only auto-assigned when the tenant has no active
        // carrier of it yet (a tenant that reflagged another type keeps their choice).
        var hasDefaultTransport = await _db.ActivityTypes
            .AnyAsync(t => t.TenantId == tenantId && t.IsActive && t.IsSystemDefaultTransport, cancellationToken);

        foreach (var seed in Defaults)
        {
            if (existingCodes.Contains(seed.Code))
            {
                continue;
            }

            var takeDefaultFlag = seed.IsDefaultTransport && !hasDefaultTransport;
            hasDefaultTransport |= takeDefaultFlag;
            _db.ActivityTypes.Add(new ActivityType
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = seed.Code,
                Name = seed.Name,
                Icon = seed.Icon,
                SortOrder = seed.SortOrder,
                KpiCategory = seed.KpiCategory,
                HasStops = seed.HasStops,
                SupportsGoods = seed.SupportsGoods,
                PlanningRelevant = seed.PlanningRelevant,
                WarehouseRelevant = seed.WarehouseRelevant,
                AllowsDuration = seed.AllowsDuration,
                IsQuickStart = seed.IsQuickStart,
                QuickStartOrder = seed.QuickStartOrder,
                IsSystemDefaultTransport = takeDefaultFlag,
                IsActive = true,
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
