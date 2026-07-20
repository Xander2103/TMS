using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Search.Services;

/// <summary>One hit in the global (Ctrl+K) search; Route is the FE path to open.</summary>
public record SearchHitDto(string Category, Guid Id, string Title, string? Subtitle, string Route);

public interface IGlobalSearchService
{
    Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Tenant-scoped cross-entity search behind the command palette. Every category is gated on
/// the same view permission its list page requires — a user only ever sees hits they could
/// open anyway. Per category the top matches are returned; ranking is left to the client.
/// </summary>
public class GlobalSearchService : IGlobalSearchService
{
    private const int PerCategory = 5;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPermissionSetService _permissionSetService;

    public GlobalSearchService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IPermissionSetService permissionSetService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _permissionSetService = permissionSetService;
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var term = query.Trim().ToLowerInvariant();
        if (term.Length < 2 || _currentUserContext.CurrentUserId is not { } userId)
        {
            return [];
        }

        var tenantId = _tenantContext.TenantId;

        // One roundtrip for the user's whole permission set instead of a query per category.
        var permissions = await _permissionSetService.GetPermissionCodesAsync(userId, cancellationToken);
        bool Can(params string[] anyOf) => anyOf.Any(permissions.Contains);

        var hits = new List<SearchHitDto>();

        if (Can(PermissionCodes.OrdersView, PermissionCodes.OrdersManage))
        {
            hits.AddRange((await _dbContext.TransportOrders.AsNoTracking()
                    .Where(o => o.TenantId == tenantId
                                && (o.OrderNumber.ToLower().Contains(term)
                                    || (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term))))
                    .OrderByDescending(o => o.OrderDate)
                    .Take(PerCategory)
                    .Select(o => new { o.Id, o.OrderNumber, o.GoodsDescription, o.Status })
                    .ToListAsync(cancellationToken))
                .Select(o => new SearchHitDto("Transportopdrachten", o.Id, o.OrderNumber,
                    o.GoodsDescription ?? o.Status.ToString(), $"/transport-orders/{o.Id}")));
        }

        if (Can(PermissionCodes.CustomersView))
        {
            hits.AddRange((await _dbContext.Customers.AsNoTracking()
                    .Where(c => c.TenantId == tenantId
                                && (c.Name.ToLower().Contains(term) || c.CustomerNumber.ToLower().Contains(term)))
                    .OrderBy(c => c.Name)
                    .Take(PerCategory)
                    .Select(c => new { c.Id, c.Name, c.CustomerNumber })
                    .ToListAsync(cancellationToken))
                .Select(c => new SearchHitDto("Klanten", c.Id, c.Name, c.CustomerNumber, $"/customers/{c.Id}")));
        }

        if (Can(PermissionCodes.DossiersView, PermissionCodes.DossiersManage))
        {
            hits.AddRange((await _dbContext.TransportDossiers.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                                && (d.DossierNumber.ToLower().Contains(term) || d.Title.ToLower().Contains(term)))
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(PerCategory)
                    .Select(d => new { d.Id, d.DossierNumber, d.Title })
                    .ToListAsync(cancellationToken))
                .Select(d => new SearchHitDto("Dossiers", d.Id, d.DossierNumber, d.Title, $"/dossiers/{d.Id}")));
        }

        if (Can(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage))
        {
            hits.AddRange((await _dbContext.Incidents.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.Title.ToLower().Contains(term))
                    .OrderByDescending(i => i.CreatedAt)
                    .Take(PerCategory)
                    .Select(i => new { i.Id, i.Title, i.Status })
                    .ToListAsync(cancellationToken))
                .Select(i => new SearchHitDto("Incidenten", i.Id, i.Title, i.Status.ToString(), $"/incidents/{i.Id}")));
        }

        if (Can(PermissionCodes.DriversView))
        {
            hits.AddRange((await _dbContext.Drivers.AsNoTracking()
                    .Where(d => d.TenantId == tenantId)
                    .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                        (d, e) => new { d.Id, d.DriverNumber, e.FirstName, e.LastName })
                    .Where(x => x.DriverNumber.ToLower().Contains(term)
                                || (x.FirstName + " " + x.LastName).ToLower().Contains(term))
                    .OrderBy(x => x.LastName)
                    .Take(PerCategory)
                    .ToListAsync(cancellationToken))
                .Select(d => new SearchHitDto("Chauffeurs", d.Id, $"{d.FirstName} {d.LastName}", d.DriverNumber, $"/drivers/{d.Id}")));
        }

        if (Can(PermissionCodes.VehiclesView))
        {
            hits.AddRange((await _dbContext.Vehicles.AsNoTracking()
                    .Where(v => v.TenantId == tenantId
                                && (v.LicensePlate.ToLower().Contains(term) || v.InternalNumber.ToLower().Contains(term)))
                    .OrderBy(v => v.InternalNumber)
                    .Take(PerCategory)
                    .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                    .ToListAsync(cancellationToken))
                .Select(v => new SearchHitDto("Voertuigen", v.Id, v.LicensePlate, v.InternalNumber, $"/vehicles/{v.Id}")));
        }

        if (Can(PermissionCodes.TrailersView))
        {
            hits.AddRange((await _dbContext.Trailers.AsNoTracking()
                    .Where(t => t.TenantId == tenantId
                                && (t.LicensePlate.ToLower().Contains(term) || t.InternalNumber.ToLower().Contains(term)))
                    .OrderBy(t => t.InternalNumber)
                    .Take(PerCategory)
                    .Select(t => new { t.Id, t.InternalNumber, t.LicensePlate })
                    .ToListAsync(cancellationToken))
                .Select(t => new SearchHitDto("Opleggers", t.Id, t.LicensePlate, t.InternalNumber, $"/trailers/{t.Id}")));
        }

        if (Can(PermissionCodes.LocationsView))
        {
            hits.AddRange((await _dbContext.Locations.AsNoTracking()
                    .Where(l => l.TenantId == tenantId
                                && (l.Name.ToLower().Contains(term) || l.Code.ToLower().Contains(term)
                                    || (l.City != null && l.City.ToLower().Contains(term))))
                    .OrderBy(l => l.Name)
                    .Take(PerCategory)
                    .Select(l => new { l.Id, l.Name, l.City })
                    .ToListAsync(cancellationToken))
                .Select(l => new SearchHitDto("Locaties", l.Id, l.Name, l.City, $"/locations/{l.Id}")));
        }

        if (Can(PermissionCodes.InvoicesView))
        {
            hits.AddRange((await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.InvoiceNumber.ToLower().Contains(term))
                    .OrderByDescending(i => i.InvoiceDate)
                    .Take(PerCategory)
                    .Select(i => new { i.Id, i.InvoiceNumber, i.Status })
                    .ToListAsync(cancellationToken))
                .Select(i => new SearchHitDto("Facturen", i.Id, i.InvoiceNumber, i.Status.ToString(), $"/invoices/{i.Id}")));
        }

        if (Can(PermissionCodes.EmployeesView))
        {
            hits.AddRange((await _dbContext.Employees.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                                && ((e.FirstName + " " + e.LastName).ToLower().Contains(term)
                                    || e.EmployeeNumber.ToLower().Contains(term)))
                    .OrderBy(e => e.LastName)
                    .Take(PerCategory)
                    .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
                    .ToListAsync(cancellationToken))
                .Select(e => new SearchHitDto("Personeel", e.Id, $"{e.FirstName} {e.LastName}", e.EmployeeNumber, $"/employees/{e.Id}")));
        }

        if (Can(PermissionCodes.PackagesView, PermissionCodes.PackagesManage))
        {
            hits.AddRange((await _dbContext.Packages.AsNoTracking()
                    .Where(p => p.TenantId == tenantId && p.PackageNumber.ToLower().Contains(term))
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(PerCategory)
                    .Select(p => new { p.Id, p.PackageNumber, p.CurrentLifecycleStatus })
                    .ToListAsync(cancellationToken))
                .Select(p => new SearchHitDto("Colli", p.Id, p.PackageNumber, p.CurrentLifecycleStatus.ToString(), $"/packages/{p.Id}")));
        }

        return hits;
    }
}
