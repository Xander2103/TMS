using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// Dossier search sprint 2026-09-23 — the server-side search behind the dossier list.
///
/// Shape: narrow tenant-scoped <c>IQueryable</c> → structural filters (direct columns) →
/// relational filters as correlated EXISTS on indexed join columns (dossier_orders,
/// dossier_activities, trips/trip_orders, transport_order_stops, invoice_lines) →
/// <c>CountAsync</c> → whitelisted ORDER BY + stable secondary key → Skip/Take → the shared list
/// projection → page-scoped hydration (stops, trips, invoices, CMR) keyed by the page's ids.
/// Never a client-side pass over the table. Text search is <c>LOWER(col) LIKE %term%</c>
/// (case-insensitive on PostgreSQL and SQLite alike; a trigram index is a later option).
/// </summary>
public partial class DossierService
{
    private static readonly HashSet<string> SortKeys = ["number", "date", "customer", "status", "confirmedat", "planningdate", "createdat"];

    public async Task<PagedResult<DossierListItemDto>> SearchAsync(DossierSearchQuery query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var page = PageRequest.Of(query.Page, query.PageSize);
        ValidateRange(query.DateFrom, query.DateTo, "dateFrom");
        ValidateRange(query.ConfirmedFrom, query.ConfirmedTo, "confirmedFrom");
        ValidateRange(query.CreatedFrom, query.CreatedTo, "createdFrom");
        ValidateRange(query.PlanningFrom, query.PlanningTo, "planningFrom");

        var dossiers = _dbContext.TransportDossiers.AsNoTracking().Where(d => d.TenantId == tenantId);

        // ---- lifecycle
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!EnumParsing.TryParseDefined<DossierStatus>(query.Status, out var status))
            {
                throw new DomainValidationException("status", "Onbekende dossierstatus.");
            }

            dossiers = dossiers.Where(d => d.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.ConfirmationSource))
        {
            if (!EnumParsing.TryParseDefined<DossierConfirmationSource>(query.ConfirmationSource, out var source))
            {
                throw new DomainValidationException("confirmationSource", "Onbekende bevestigingsbron.");
            }

            dossiers = dossiers.Where(d => d.Status == DossierStatus.Closed && d.ConfirmationSource == source);
        }

        if (query.ConfirmedFrom is { } cf)
        {
            var from = cf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            dossiers = dossiers.Where(d => d.Status == DossierStatus.Closed && d.ClosedAt >= from);
        }

        if (query.ConfirmedTo is { } ct)
        {
            var to = ct.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            dossiers = dossiers.Where(d => d.Status == DossierStatus.Closed && d.ClosedAt < to);
        }

        if (query.CreatedFrom is { } crf)
        {
            var from = crf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            dossiers = dossiers.Where(d => d.CreatedAt >= from);
        }

        if (query.CreatedTo is { } crt)
        {
            var to = crt.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            dossiers = dossiers.Where(d => d.CreatedAt < to);
        }

        // ---- identification / customer / dossier date
        if (query.CustomerId is { } customerId) dossiers = dossiers.Where(d => d.CustomerId == customerId);
        if (query.DateFrom is { } df) dossiers = dossiers.Where(d => d.DossierDate >= df);
        if (query.DateTo is { } dt) dossiers = dossiers.Where(d => d.DossierDate <= dt);
        if (Term(query.DossierNumber) is { } number) dossiers = dossiers.Where(d => d.DossierNumber.ToLower().Contains(number));
        if (Term(query.CustomerReference) is { } reference)
        {
            dossiers = dossiers.Where(d =>
                (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(reference))
                || _dbContext.DossierOrders.Any(l => l.DossierId == d.Id
                    && _dbContext.TransportOrders.Any(o => o.Id == l.TransportOrderId && o.CustomerReference != null && o.CustomerReference.ToLower().Contains(reference))));
        }

        if (Term(query.CustomerNumber) is { } customerNumber)
        {
            dossiers = dossiers.Where(d => _dbContext.Customers.Any(c => c.Id == d.CustomerId && c.CustomerNumber.ToLower().Contains(customerNumber)));
        }

        if (Term(query.OrderNumber) is { } orderNumber)
        {
            dossiers = dossiers.Where(d => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o => o.OrderNumber.ToLower().Contains(orderNumber)));
        }

        // ---- planning (trips of the dossier's orders; cancelled trips never count)
        if (query.DriverId is { } driverId) dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t => t.DriverId == driverId));
        if (query.VehicleId is { } vehicleId) dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t => t.VehicleId == vehicleId));
        if (query.TrailerId is { } trailerId) dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t => t.TrailerId == trailerId));
        if (Term(query.LicensePlate) is { } plate)
        {
            dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t =>
                _dbContext.Vehicles.Any(v => v.Id == t.VehicleId && v.LicensePlate.ToLower().Contains(plate))
                || _dbContext.Trailers.Any(tr => tr.Id == t.TrailerId && tr.LicensePlate != null && tr.LicensePlate.ToLower().Contains(plate))));
        }

        if (query.PlanningFrom is { } pf)
        {
            dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t => t.TripDate >= pf && (query.PlanningTo == null || t.TripDate <= query.PlanningTo))
                || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null
                    && a.PlannedDate >= pf && (query.PlanningTo == null || a.PlannedDate <= query.PlanningTo)));
        }
        else if (query.PlanningTo is { } pt)
        {
            dossiers = dossiers.Where(d => _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t => t.TripDate <= pt)
                || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate <= pt));
        }

        // ---- transport
        if (query.ActivityTypeId is { } typeId)
        {
            dossiers = dossiers.Where(d => _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.ActivityTypeId == typeId));
        }

        if (Term(query.LoadingCity) is { } loading)
        {
            dossiers = dossiers.Where(d => _dbContext.TransportOrderStops.Where(s => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == s.TransportOrderId)).Any(s => s.StopType == StopType.Loading && s.City != null && s.City.ToLower().Contains(loading)));
        }

        if (Term(query.UnloadingCity) is { } unloading)
        {
            dossiers = dossiers.Where(d => _dbContext.TransportOrderStops.Where(s => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == s.TransportOrderId)).Any(s => (s.StopType == StopType.Unloading || s.StopType == StopType.Site) && s.City != null && s.City.ToLower().Contains(unloading)));
        }

        if (Term(query.PostalCode) is { } postal)
        {
            dossiers = dossiers.Where(d => _dbContext.TransportOrderStops.Where(s => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == s.TransportOrderId)).Any(s => s.PostalCode != null && s.PostalCode.ToLower().StartsWith(postal)));
        }

        if (Term(query.CountryCode) is { } country)
        {
            dossiers = dossiers.Where(d => _dbContext.TransportOrderStops.Where(s => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == s.TransportOrderId)).Any(s => s.CountryCode != null && s.CountryCode.ToLower() == country));
        }

        // ---- commercial
        if (!string.IsNullOrWhiteSpace(query.PriceStatus))
        {
            dossiers = query.PriceStatus.Trim().ToLowerInvariant() switch
            {
                "priced" => dossiers.Where(d => (_dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && x.t.HasStops).Join(_dbContext.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o).Where(OrderPricingState.IsPricedExpression).Count() + _dbContext.DossierActivities.Where(a => a.DossierId == d.Id).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && !x.t.HasStops).Join(_dbContext.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p).Where(ActivityPricingState.IsPricedExpression).Count()) > 0),
                "unpriced" => dossiers.Where(d => (_dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && x.t.HasStops).Join(_dbContext.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o).Where(OrderPricingState.IsPricedExpression).Count() + _dbContext.DossierActivities.Where(a => a.DossierId == d.Id).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && !x.t.HasStops).Join(_dbContext.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p).Where(ActivityPricingState.IsPricedExpression).Count()) == 0),
                "partial" => dossiers.Where(d => (_dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && x.t.HasStops).Join(_dbContext.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o).Where(OrderPricingState.IsPricedExpression).Count() + _dbContext.DossierActivities.Where(a => a.DossierId == d.Id).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && !x.t.HasStops).Join(_dbContext.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p).Where(ActivityPricingState.IsPricedExpression).Count()) > 0 && (_dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && x.t.HasStops).Join(_dbContext.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o).Where(OrderPricingState.IsPricedExpression).Count() + _dbContext.DossierActivities.Where(a => a.DossierId == d.Id).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t }).Where(x => x.t.IsBillable && !x.t.HasStops).Join(_dbContext.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p).Where(ActivityPricingState.IsPricedExpression).Count()) < _dbContext.DossierActivities.Where(a => a.DossierId == d.Id).Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => t).Count(t => t.IsBillable)),
                _ => throw new DomainValidationException("priceStatus", "Onbekende prijsstatus."),
            };
        }

        if (!string.IsNullOrWhiteSpace(query.InvoiceStatus))
        {
            dossiers = query.InvoiceStatus.Trim().ToLowerInvariant() switch
            {
                "notinvoiced" => dossiers.Where(d => !_dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any()),
                "draft" => dossiers.Where(d => _dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any(i => i.Status == InvoiceStatus.Draft) && !_dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any(i => i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid)),
                "sent" => dossiers.Where(d => _dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any(i => i.Status == InvoiceStatus.Sent) && !_dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any(i => i.Status == InvoiceStatus.Paid)),
                "paid" => dossiers.Where(d => _dbContext.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled && _dbContext.InvoiceLines.Any(l2 => l2.InvoiceId == i.Id && ((l2.TransportOrderId != null && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == l2.TransportOrderId)) || (l2.DossierActivityId != null && _dbContext.DossierActivities.Any(a2 => a2.DossierId == d.Id && a2.Id == l2.DossierActivityId))))).Any(i => i.Status == InvoiceStatus.Paid)),
                _ => throw new DomainValidationException("invoiceStatus", "Onbekende factuurstatus."),
            };
        }

        if (query.HasCmr is { } hasCmr)
        {
            dossiers = hasCmr ? dossiers.Where(d => (_dbContext.TransportOrderDocuments.Any(doc => doc.DocumentType == TransportOrderDocumentType.Cmr && (doc.DossierId == d.Id || (doc.DossierId == null && doc.TransportOrderId != null && _dbContext.DossierOrders.Any(l3 => l3.DossierId == d.Id && l3.TransportOrderId == doc.TransportOrderId)))) || _dbContext.IssuedTransportDocuments.Any(doc => doc.DossierId == d.Id && doc.Kind == IssuedTransportDocumentKind.Cmr))) : dossiers.Where(d => !(_dbContext.TransportOrderDocuments.Any(doc => doc.DocumentType == TransportOrderDocumentType.Cmr && (doc.DossierId == d.Id || (doc.DossierId == null && doc.TransportOrderId != null && _dbContext.DossierOrders.Any(l3 => l3.DossierId == d.Id && l3.TransportOrderId == doc.TransportOrderId)))) || _dbContext.IssuedTransportDocuments.Any(doc => doc.DossierId == d.Id && doc.Kind == IssuedTransportDocumentKind.Cmr)));
        }

        // ---- global search (one OR-predicate of correlated EXISTS, all on indexed join columns)
        if (Term(query.Search) is { } term)
        {
            dossiers = dossiers.Where(d =>
                d.DossierNumber.ToLower().Contains(term)
                || d.Title.ToLower().Contains(term)
                || (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(term))
                || _dbContext.Customers.Any(c => c.Id == d.CustomerId && (c.Name.ToLower().Contains(term) || c.CustomerNumber.ToLower().Contains(term)))
                || _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o => o.OrderNumber.ToLower().Contains(term) || (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term)))
                || _dbContext.TransportOrderStops.Where(s => _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == s.TransportOrderId)).Any(s => (s.City != null && s.City.ToLower().Contains(term)) || (s.PostalCode != null && s.PostalCode.ToLower().StartsWith(term)))
                || _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Any(t =>
                    _dbContext.Vehicles.Any(v => v.Id == t.VehicleId && v.LicensePlate.ToLower().Contains(term))
                    || _dbContext.Drivers.Any(dr => dr.Id == t.DriverId
                        && _dbContext.Employees.Any(e => e.Id == dr.EmployeeId && (e.FirstName + " " + e.LastName).ToLower().Contains(term)))));
        }

        var totalCount = await dossiers.CountAsync(cancellationToken);
        var ordered = ApplySort(dossiers, query.Sort, query.Dir);
        var items = await ProjectListAsync(ordered.Skip(page.Skip).Take(page.PageSize), cancellationToken);
        items = await HydratePageAsync(items, cancellationToken);
        return new PagedResult<DossierListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    private static string? Term(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static void ValidateRange(DateOnly? from, DateOnly? to, string field)
    {
        if (from is { } f && to is { } t && f > t)
        {
            throw new DomainValidationException(field, "De begindatum ligt na de einddatum.");
        }
    }

    /// <summary>Whitelisted sort keys; a stable DossierNumber secondary key keeps pagination consistent.</summary>
    private IOrderedQueryable<TransportDossier> ApplySort(IQueryable<TransportDossier> query, string? sort, string? dir)
    {
        var key = (sort ?? "date").Trim().ToLowerInvariant();
        if (!SortKeys.Contains(key)) key = "date";
        var descending = string.IsNullOrWhiteSpace(dir)
            ? key is "date" or "confirmedat" or "planningdate" or "createdat"
            : string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<TransportDossier> ordered = key switch
        {
            "number" => descending ? query.OrderByDescending(d => d.DossierNumber) : query.OrderBy(d => d.DossierNumber),
            "customer" => descending
                ? query.OrderByDescending(d => _dbContext.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.Name).FirstOrDefault())
                : query.OrderBy(d => _dbContext.Customers.Where(c => c.Id == d.CustomerId).Select(c => c.Name).FirstOrDefault()),
            "status" => descending ? query.OrderByDescending(d => d.Status) : query.OrderBy(d => d.Status),
            // Nulls last in both directions (providers disagree on NULL ordering).
            "confirmedat" => descending
                ? query.OrderBy(d => d.Status != DossierStatus.Closed || d.ClosedAt == null).ThenByDescending(d => d.ClosedAt)
                : query.OrderBy(d => d.Status != DossierStatus.Closed || d.ClosedAt == null).ThenBy(d => d.ClosedAt),
            "planningdate" => descending
                ? query.OrderBy(d => (_dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) == null ? _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) == null ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) < _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate)) == null).ThenByDescending(d => (_dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) == null ? _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) == null ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) < _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate)))
                : query.OrderBy(d => (_dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) == null ? _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) == null ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) < _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate)) == null).ThenBy(d => (_dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) == null ? _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) == null ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) < _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate) ? _dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled && _dbContext.TripOrders.Any(to => to.TripId == t.Id && _dbContext.TransportOrders.Where(o => _dbContext.DossierOrders.Any(l => l.DossierId == d.Id && l.TransportOrderId == o.Id) || _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == o.Id)).Any(o2 => o2.Id == to.TransportOrderId))).Min(t2 => (DateOnly?)t2.TripDate) : _dbContext.DossierActivities.Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null && a.PlannedDate != null).Min(a => a.PlannedDate))),
            "createdat" => descending ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt),
            _ => descending
                ? query.OrderBy(d => d.DossierDate == null).ThenByDescending(d => d.DossierDate)
                : query.OrderBy(d => d.DossierDate == null).ThenBy(d => d.DossierDate),
        };
        return ordered.ThenBy(d => d.DossierNumber).ThenBy(d => d.Id);
    }

    // ------------------------------------------------------------ page-scoped hydration

    private async Task<List<DossierListItemDto>> HydratePageAsync(List<DossierListItemDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;
        var tenantId = _tenantContext.TenantId;
        var dossierIds = items.Select(i => i.Id).ToList();

        var links = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && dossierIds.Contains(l.DossierId))
            .Select(l => new { l.DossierId, l.TransportOrderId })
            .Union(_dbContext.DossierActivities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && dossierIds.Contains(a.DossierId) && a.LinkedTransportOrderId != null)
                .Select(a => new { a.DossierId, TransportOrderId = a.LinkedTransportOrderId!.Value }))
            .ToListAsync(cancellationToken);
        var orderIds = links.Select(l => l.TransportOrderId).Distinct().ToList();
        var dossiersByOrder = links.ToLookup(l => l.TransportOrderId, l => l.DossierId);

        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderDate, o.OrderNumber })
            .ToListAsync(cancellationToken);
        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
            .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId), s => s.LocationId, l => l.Id, (s, ls) => new { s, ls })
            .SelectMany(x => x.ls.DefaultIfEmpty(), (x, l) => new { x.s.TransportOrderId, x.s.Sequence, x.s.StopType, City = x.s.City ?? (l != null ? l.City : null) })
            .ToListAsync(cancellationToken);
        var trips = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && orderIds.Contains(to.TransportOrderId))
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.Status != TripStatus.Cancelled), to => to.TripId, t => t.Id,
                (to, t) => new { to.TransportOrderId, t.TripDate, t.DriverId, t.VehicleId })
            .ToListAsync(cancellationToken);
        var driverIds = trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var driverNames = driverIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var vehicleIds = trips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var plates = vehicleIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.LicensePlate, cancellationToken);
        var activityDates = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && dossierIds.Contains(a.DossierId) && a.LinkedTransportOrderId == null && a.PlannedDate != null)
            .Select(a => new { a.DossierId, a.PlannedDate })
            .ToListAsync(cancellationToken);
        var invoiceStatuses = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId
                        && ((l.TransportOrderId != null && orderIds.Contains(l.TransportOrderId.Value))
                            || (l.DossierActivityId != null && _dbContext.DossierActivities.Any(a => a.Id == l.DossierActivityId && dossierIds.Contains(a.DossierId)))))
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Cancelled), l => l.InvoiceId, i => i.Id,
                (l, i) => new { l.TransportOrderId, l.DossierActivityId, i.Status })
            .ToListAsync(cancellationToken);
        var activityDossier = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && dossierIds.Contains(a.DossierId))
            .Select(a => new { a.Id, a.DossierId })
            .ToDictionaryAsync(a => a.Id, a => a.DossierId, cancellationToken);
        var cmrDossiers = (await _dbContext.TransportOrderDocuments.AsNoTracking()
                .Where(doc => doc.TenantId == tenantId && doc.DocumentType == TransportOrderDocumentType.Cmr
                              && ((doc.DossierId != null && dossierIds.Contains(doc.DossierId.Value))
                                  || (doc.DossierId == null && doc.TransportOrderId != null && orderIds.Contains(doc.TransportOrderId.Value))))
                .Select(doc => new { doc.DossierId, doc.TransportOrderId })
                .ToListAsync(cancellationToken))
            .SelectMany(doc => doc.DossierId is { } did ? [did] : dossiersByOrder[doc.TransportOrderId!.Value])
            .Concat(await _dbContext.IssuedTransportDocuments.AsNoTracking()
                .Where(doc => doc.TenantId == tenantId && doc.Kind == IssuedTransportDocumentKind.Cmr && doc.DossierId != null && dossierIds.Contains(doc.DossierId.Value))
                .Select(doc => doc.DossierId!.Value).ToListAsync(cancellationToken))
            .ToHashSet();

        var ordersByDossier = links.ToLookup(l => l.DossierId, l => l.TransportOrderId);
        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);
        var tripsByOrder = trips.ToLookup(t => t.TransportOrderId);
        var activityDatesByDossier = activityDates.ToLookup(a => a.DossierId, a => a.PlannedDate!.Value);

        return items.Select(item =>
        {
            var dossierOrders = orders.Where(o => ordersByDossier[item.Id].Contains(o.Id)).OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber).ToList();
            var first = dossierOrders.FirstOrDefault();
            var last = dossierOrders.LastOrDefault();
            var loading = first is null ? null : stopsByOrder[first.Id].Where(s => s.StopType == StopType.Loading).OrderBy(s => s.Sequence).FirstOrDefault()?.City;
            var unloading = last is null ? null : stopsByOrder[last.Id].Where(s => s.StopType is StopType.Unloading or StopType.Site).OrderByDescending(s => s.Sequence).FirstOrDefault()?.City;

            var dossierTrips = dossierOrders.SelectMany(o => tripsByOrder[o.Id]).ToList();
            var drivers = dossierTrips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
            var vehicles = dossierTrips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value).Distinct().ToList();
            var planning = dossierTrips.Select(t => (DateOnly?)t.TripDate).Concat(activityDatesByDossier[item.Id].Select(d => (DateOnly?)d)).Where(d => d != null).Min();

            var dossierOrderIds = dossierOrders.Select(o => o.Id).ToHashSet();
            var statuses = invoiceStatuses
                .Where(l => (l.TransportOrderId is { } oid && dossierOrderIds.Contains(oid))
                            || (l.DossierActivityId is { } aid && activityDossier.TryGetValue(aid, out var did) && did == item.Id))
                .Select(l => l.Status).ToList();
            var invoiceStatus = statuses.Count == 0 ? "NotInvoiced"
                : statuses.Contains(InvoiceStatus.Paid) ? "Paid"
                : statuses.Contains(InvoiceStatus.Sent) ? "Sent"
                : "Draft";

            return item with
            {
                FirstLoadingCity = loading,
                LastUnloadingCity = unloading,
                DriverSummary = drivers.Count switch { 0 => null, 1 => driverNames.GetValueOrDefault(drivers[0]), var n => $"{n} chauffeurs" },
                VehicleSummary = vehicles.Count switch { 0 => null, 1 => plates.GetValueOrDefault(vehicles[0]), var n => $"{n} voertuigen" },
                PlanningDate = planning,
                InvoiceStatus = invoiceStatus,
                HasCmr = cmrDossiers.Contains(item.Id),
            };
        }).ToList();
    }
}
