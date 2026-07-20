using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Portal.Services;

public interface IResourceLinkService
{
    Task<IReadOnlyList<ResourceLinkDto>> ListMineAsync(ResourceLinkKind? kind, CancellationToken cancellationToken);
    Task<ResourceLinkDto> TouchAsync(TouchResourceLinkRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>
/// Favorites / recents / pins for the signed-in user. Self-scoped (no permission codes on the
/// endpoints), but LISTING rechecks the per-entity-type view permission and drops links whose
/// target disappeared — a stale link never leaks a resource the user may no longer see.
/// </summary>
public class ResourceLinkService : IResourceLinkService
{
    private const int RecentCap = 25;

    /// <summary>Closed catalog: entity type → any-of view permissions (empty = signed-in is enough).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Catalog =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Customer"] = [PermissionCodes.CustomersView],
            ["Location"] = [PermissionCodes.LocationsView],
            ["Driver"] = [PermissionCodes.DriversView],
            ["Vehicle"] = [PermissionCodes.VehiclesView],
            ["Trailer"] = [PermissionCodes.TrailersView],
            ["TransportOrder"] = [PermissionCodes.OrdersView, PermissionCodes.OrdersManage],
            ["Trip"] = [PermissionCodes.PlanningView],
            ["TransportDossier"] = [PermissionCodes.DossiersView],
            ["Incident"] = [PermissionCodes.IncidentsView],
            ["Report"] = [PermissionCodes.ReportsView],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPermissionSetService _permissionSetService;
    private readonly TimeProvider _timeProvider;

    public ResourceLinkService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IPermissionSetService permissionSetService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _permissionSetService = permissionSetService;
        _timeProvider = timeProvider;
    }

    private Guid RequireUserId() => _currentUserContext.CurrentUserId
        ?? throw new DomainValidationException("Er is geen aangemelde gebruiker.");

    private IQueryable<UserResourceLink> Mine(Guid userId) =>
        _dbContext.UserResourceLinks.Where(l => l.TenantId == _tenantContext.TenantId && l.UserId == userId);

    public async Task<IReadOnlyList<ResourceLinkDto>> ListMineAsync(
        ResourceLinkKind? kind, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var query = Mine(userId).AsNoTracking();
        if (kind is { } k)
        {
            query = query.Where(l => l.Kind == k);
        }

        var links = await query
            .OrderBy(l => l.SortOrder)
            .ThenByDescending(l => l.TouchedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        // Permission recheck: hide types the user can no longer view.
        var permissions = await _permissionSetService.GetPermissionCodesAsync(userId, cancellationToken);
        links = links
            .Where(l => Catalog.TryGetValue(l.EntityType, out var required)
                        && (required.Length == 0 || required.Any(permissions.Contains)))
            .ToList();

        // Existence recheck: drop links whose target was deleted (batched per type).
        var alive = await ResolveExistingIdsAsync(links, cancellationToken);
        return links
            .Where(l => l.EntityType == "Report" || alive.Contains((l.EntityType, l.EntityId)))
            .Select(Map)
            .ToList();
    }

    public async Task<ResourceLinkDto> TouchAsync(TouchResourceLinkRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();

        if (!Catalog.ContainsKey(request.EntityType))
        {
            throw new DomainValidationException("entityType", "Onbekend type resource.");
        }

        if (string.IsNullOrWhiteSpace(request.Label) || string.IsNullOrWhiteSpace(request.Route))
        {
            throw new DomainValidationException("Label en route zijn verplicht.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var link = await Mine(userId).FirstOrDefaultAsync(
            l => l.Kind == request.Kind && l.EntityType == request.EntityType && l.EntityId == request.EntityId,
            cancellationToken);

        if (link is null)
        {
            link = new UserResourceLink
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Kind = request.Kind,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                SortOrder = int.MaxValue, // new favorites/pins land at the end until reordered
            };
            _dbContext.UserResourceLinks.Add(link);
        }

        link.Label = request.Label.Trim();
        link.Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim();
        link.Route = request.Route.Trim();
        link.TouchedAt = now;

        if (request.Kind == ResourceLinkKind.Recent)
        {
            // Bounded history: everything beyond the cap falls off (soft delete).
            var overflow = await Mine(userId)
                .Where(l => l.Kind == ResourceLinkKind.Recent && l.Id != link.Id)
                .OrderByDescending(l => l.TouchedAt)
                .Skip(RecentCap - 1)
                .ToListAsync(cancellationToken);
            _dbContext.RemoveRange(overflow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(link);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var link = await Mine(userId).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return false;
        }

        _dbContext.Remove(link); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var links = await Mine(userId).Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken);
        var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        foreach (var link in links)
        {
            link.SortOrder = order[link.Id];
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>(EntityType, EntityId) pairs that still exist, one query per distinct type.</summary>
    private async Task<HashSet<(string, Guid)>> ResolveExistingIdsAsync(
        IReadOnlyList<UserResourceLink> links, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var alive = new HashSet<(string, Guid)>();
        foreach (var group in links.GroupBy(l => l.EntityType))
        {
            var ids = group.Select(l => l.EntityId).Distinct().ToList();
            var found = group.Key switch
            {
                "Customer" => await _dbContext.Customers.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Location" => await _dbContext.Locations.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Driver" => await _dbContext.Drivers.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Vehicle" => await _dbContext.Vehicles.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Trailer" => await _dbContext.Trailers.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "TransportOrder" => await _dbContext.TransportOrders.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Trip" => await _dbContext.Trips.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "TransportDossier" => await _dbContext.TransportDossiers.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                "Incident" => await _dbContext.Incidents.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken),
                _ => [],
            };
            foreach (var id in found)
            {
                alive.Add((group.Key, id));
            }
        }

        return alive;
    }

    private static ResourceLinkDto Map(UserResourceLink l) =>
        new(l.Id, l.Kind, l.EntityType, l.EntityId, l.Label, l.Subtitle, l.Route, l.SortOrder, l.TouchedAt);
}
