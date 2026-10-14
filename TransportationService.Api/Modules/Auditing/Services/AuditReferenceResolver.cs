using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Services;

/// <summary>
/// B25: turns the ids inside audit values into what a planner recognises — an order number, a
/// customer or user name, a trip or dossier number. Batched per page (one query per entity
/// set that actually has candidate ids), tenant-scoped, and lossless: an id that resolves to
/// nothing simply stays unresolved, so the presentation falls back to a neutral word and the
/// raw audit record is never altered.
/// </summary>
public interface IAuditReferenceResolver
{
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IEnumerable<string?> valueJsons, CancellationToken cancellationToken);
}

public class AuditReferenceResolver : IAuditReferenceResolver
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;

    public AuditReferenceResolver(TransportationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Every GUID found in the values (top level, one nesting level and arrays).</summary>
    public static HashSet<Guid> CollectIds(IEnumerable<string?> valueJsons)
    {
        var ids = new HashSet<Guid>();
        foreach (var json in valueJsons)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                Walk(doc.RootElement, ids, 0);
            }
            catch (JsonException)
            {
                // A malformed historic payload simply contributes nothing.
            }
        }

        return ids;
    }

    private static void Walk(JsonElement element, HashSet<Guid> ids, int depth)
    {
        if (depth > 3) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.String when Guid.TryParse(element.GetString(), out var id) && id != Guid.Empty:
                ids.Add(id);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Walk(property.Value, ids, depth + 1);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, ids, depth + 1);
                break;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IEnumerable<string?> valueJsons, CancellationToken cancellationToken)
    {
        var ids = CollectIds(valueJsons);
        var labels = new Dictionary<Guid, string>();
        if (ids.Count == 0) return labels;
        var t = _tenant.TenantId;
        var list = ids.ToList();

        async Task Add<TEntity>(IQueryable<TEntity> source, Func<IQueryable<TEntity>, IQueryable<KeyValuePair<Guid, string>>> project)
        {
            var missing = list.Where(id => !labels.ContainsKey(id)).ToList();
            if (missing.Count == 0) return;
            foreach (var pair in await project(source).ToListAsync(cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(pair.Value)) labels.TryAdd(pair.Key, pair.Value);
            }
        }

        await Add(_db.TransportOrders.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.OrderNumber)));
        await Add(_db.TransportDossiers.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.DossierNumber)));
        await Add(_db.Trips.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.TripNumber)));
        await Add(_db.Customers.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.Name)));
        await Add(_db.Users.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, (x.FirstName + " " + x.LastName).Trim())));
        await Add(_db.Employees.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, (x.FirstName + " " + x.LastName).Trim())));
        await Add(_db.Drivers.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id))
            .Join(_db.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => new KeyValuePair<Guid, string>(d.Id, (e.FirstName + " " + e.LastName).Trim())));
        await Add(_db.Vehicles.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.InternalNumber + " (" + x.LicensePlate + ")")));
        await Add(_db.Trailers.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.InternalNumber)));
        await Add(_db.LegalEntities.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.TradingName ?? x.LegalName)));
        await Add(_db.Locations.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.Name)));
        await Add(_db.ActivityTypes.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.Name)));
        await Add(_db.DossierActivities.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id))
            .Join(_db.ActivityTypes.AsNoTracking(), a => a.ActivityTypeId, at => at.Id, (a, at) => new KeyValuePair<Guid, string>(a.Id, a.Label != null ? at.Name + " · " + a.Label : at.Name)));
        await Add(_db.Packages.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.PackageNumber)));
        await Add(_db.Invoices.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.InvoiceNumber)));
        await Add(_db.Incidents.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.Title)));
        await Add(_db.Warehouses.AsNoTracking(), q => q.Where(x => x.TenantId == t && list.Contains(x.Id)).Select(x => new KeyValuePair<Guid, string>(x.Id, x.Name)));
        return labels;
    }
}
