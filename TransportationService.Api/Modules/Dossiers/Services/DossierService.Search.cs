using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// Dossier search sprint 2026-09-23 — the server-side search behind the dossier list.
///
/// Shape: narrow tenant-scoped <c>IQueryable</c> → structural filters (direct columns) →
/// relational filters as <c>d.Id IN (uncorrelated id subquery)</c> → <c>CountAsync</c> →
/// whitelisted ORDER BY + stable secondary key → Skip/Take → the shared list projection →
/// page-scoped hydration (stops, trips, invoices, CMR) keyed by the page's ids. Never a
/// client-side pass over the table. Text search is <c>LOWER(col) LIKE %term%</c>
/// (case-insensitive on PostgreSQL and SQLite alike; a trigram index is a later option).
///
/// Performance final 2026-10-21 (PERF-BLOCKER 1): every relational predicate used to re-derive
/// "the orders of this dossier" as a correlated OR-of-EXISTS inside a derived subquery, which
/// PostgreSQL cannot decorrelate — it rescanned orders, stops and trips PER DOSSIER ROW (cost
/// 3.7e9 for a text search, 2e10 for the planning sort; both past the 30 s command timeout on a
/// 32k-dossier tenant). The predicates are now built the other way round: predicate → matching
/// order / trip / stop / invoice ids → dossier ids through BOTH link paths (dossier_orders and
/// activity-linked orders), each an uncorrelated subquery that the planner evaluates once
/// (semi-join at the top level, hashed SubPlan inside an OR). The planning date is ONE grouped
/// derived set LEFT JOINed for the sort, so the key is a plain column and the sort is a top-N
/// heapsort after the filters. Every subquery carries the tenant on tables that have it.
/// </summary>
public partial class DossierService
{
    private static readonly HashSet<string> SortKeys = ["number", "date", "customer", "status", "confirmedat", "planningdate", "createdat"];

    public async Task<PagedResult<DossierListItemDto>> SearchAsync(DossierSearchQuery query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var page = PageRequest.Of(query.Page, query.PageSize);
        var dossiers = await ComposeFilterAsync(query, tenantId, cancellationToken);

        var totalCount = await dossiers.CountAsync(cancellationToken);
        var ordered = ApplySort(dossiers, query.Sort, query.Dir, tenantId);
        var items = await ProjectListAsync(ordered.Skip(page.Skip).Take(page.PageSize), cancellationToken);
        items = await WithoutPricesUnlessPermittedAsync(await HydratePageAsync(items, cancellationToken), cancellationToken);
        return new PagedResult<DossierListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    /// <summary>The tenant-scoped dossier query with every filter of the search AND-ed on — unsorted, unexecuted.</summary>
    private async Task<IQueryable<TransportDossier>> ComposeFilterAsync(DossierSearchQuery query, Guid tenantId, CancellationToken cancellationToken)
    {
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

        // ---- B11 quick views: owner, open incidents, planned/unplanned
        if (query.ResponsibleUserId is { } responsibleUserId) dossiers = dossiers.Where(d => d.ResponsibleUserId == responsibleUserId);
        if (query.HasOpenIncident is { } hasOpenIncident)
        {
            // The same definition as the list row's OpenIncidentCount, so the filter and the badge agree.
            // A plain correlated EXISTS on incidents(dossier_id) at the top level: the planner turns it into a (anti) semi-join.
            dossiers = hasOpenIncident
                ? dossiers.Where(d => _dbContext.Incidents.Any(i => i.DossierId == d.Id && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress)))
                : dossiers.Where(d => !_dbContext.Incidents.Any(i => i.DossierId == d.Id && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress)));
        }

        if (!string.IsNullOrWhiteSpace(query.Planning))
        {
            // Planned = any of the dossier's orders sits on a non-cancelled trip, or a standalone
            // activity carries a planned date — the same facts behind the Planning column.
            var planned = PlanningRows(tenantId).Select(r => r.DossierId);
            dossiers = query.Planning.Trim().ToLowerInvariant() switch
            {
                "planned" => dossiers.Where(d => planned.Contains(d.Id)),
                "unplanned" => dossiers.Where(d => !planned.Contains(d.Id)),
                _ => throw new DomainValidationException("planning", "Onbekende planningsstatus."),
            };
        }

        // ---- identification / customer / dossier date
        if (query.CustomerId is { } customerId) dossiers = dossiers.Where(d => d.CustomerId == customerId);
        if (query.DateFrom is { } df) dossiers = dossiers.Where(d => d.DossierDate >= df);
        if (query.DateTo is { } dt) dossiers = dossiers.Where(d => d.DossierDate <= dt);
        if (Term(query.DossierNumber) is { } number) dossiers = dossiers.Where(d => d.DossierNumber.ToLower().Contains(number));
        if (Term(query.CustomerReference) is { } reference)
        {
            // The dossier's own reference, or the reference of an order linked through dossier_orders
            // (as before: the activity link path does not take part in this filter).
            var referencedOrderIds = _dbContext.TransportOrders
                .Where(o => o.TenantId == tenantId && o.CustomerReference != null && o.CustomerReference.ToLower().Contains(reference))
                .Select(o => o.Id);
            var viaOrders = _dbContext.DossierOrders
                .Where(l => l.TenantId == tenantId && referencedOrderIds.Contains(l.TransportOrderId))
                .Select(l => l.DossierId);
            dossiers = dossiers.Where(d =>
                (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(reference))
                || viaOrders.Contains(d.Id));
        }

        if (Term(query.CustomerNumber) is { } customerNumber)
        {
            dossiers = dossiers.Where(d => _dbContext.Customers.Any(c => c.Id == d.CustomerId && c.CustomerNumber.ToLower().Contains(customerNumber)));
        }

        if (Term(query.OrderNumber) is { } orderNumber)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithOrder(tenantId, o => o.OrderNumber.ToLower().Contains(orderNumber)));
        }

        // ---- planning (trips of the dossier's orders; cancelled trips never count)
        if (query.DriverId is { } driverId) dossiers = WhereDossierIn(dossiers, DossierIdsWithTrip(tenantId, t => t.DriverId == driverId));
        if (query.VehicleId is { } vehicleId) dossiers = WhereDossierIn(dossiers, DossierIdsWithTrip(tenantId, t => t.VehicleId == vehicleId));
        if (query.TrailerId is { } trailerId) dossiers = WhereDossierIn(dossiers, DossierIdsWithTrip(tenantId, t => t.TrailerId == trailerId));
        if (Term(query.LicensePlate) is { } plate)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithTrip(tenantId, t =>
                _dbContext.Vehicles.Any(v => v.Id == t.VehicleId && v.LicensePlate.ToLower().Contains(plate))
                || _dbContext.Trailers.Any(tr => tr.Id == t.TrailerId && tr.LicensePlate != null && tr.LicensePlate.ToLower().Contains(plate))));
        }

        if (query.PlanningFrom is not null || query.PlanningTo is not null)
        {
            // Any non-cancelled trip date OR any standalone activity's planned date inside the window.
            var rows = PlanningRows(tenantId);
            if (query.PlanningFrom is { } pf) rows = rows.Where(r => r.PlannedOn >= pf);
            if (query.PlanningTo is { } pt) rows = rows.Where(r => r.PlannedOn <= pt);
            dossiers = WhereDossierIn(dossiers, rows.Select(r => r.DossierId));
        }

        // ---- transport
        if (query.ActivityTypeId is { } typeId)
        {
            dossiers = dossiers.Where(d => _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.ActivityTypeId == typeId));
        }

        if (query.TransportTypeId is { } transportTypeId)
        {
            // A transport type is an activity type whose work runs through an order (HasStops).
            dossiers = dossiers.Where(d => _dbContext.DossierActivities.Any(a => a.DossierId == d.Id && a.ActivityTypeId == transportTypeId
                && _dbContext.ActivityTypes.Any(t => t.Id == a.ActivityTypeId && t.HasStops)));
        }

        if (Term(query.LoadingCity) is { } loading)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithStop(tenantId, s => s.StopType == StopType.Loading && s.City != null && s.City.ToLower().Contains(loading)));
        }

        if (Term(query.UnloadingCity) is { } unloading)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithStop(tenantId, s => (s.StopType == StopType.Unloading || s.StopType == StopType.Site) && s.City != null && s.City.ToLower().Contains(unloading)));
        }

        if (Term(query.PostalCode) is { } postal)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithStop(tenantId, s => s.PostalCode != null && s.PostalCode.ToLower().StartsWith(postal)));
        }

        if (Term(query.CountryCode) is { } country)
        {
            dossiers = WhereDossierIn(dossiers, DossierIdsWithStop(tenantId, s => s.CountryCode != null && s.CountryCode.ToLower() == country));
        }

        // ---- commercial
        if (!string.IsNullOrWhiteSpace(query.PriceStatus))
        {
            // M11: the SAME counts as the price column and the detail financials (DossierPriceCounts),
            // so the filter, the row and the dossier page can never disagree.
            dossiers = DossierPriceCounts.Filter(_dbContext, dossiers, query.PriceStatus.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(query.InvoiceStatus))
        {
            // Security M1: the invoice status is invoice data; filtering on it would reveal it.
            if (!await CanViewPricesAsync(cancellationToken))
            {
                throw new DomainValidationException("invoiceStatus", "Je hebt geen recht om op factuurstatus te filteren.");
            }

            dossiers = query.InvoiceStatus.Trim().ToLowerInvariant() switch
            {
                // M16: "Te factureren" — work that CAN be invoiced and is not on an invoice yet: a
                // completed order of the dossier, or a completed billable standalone activity whose
                // pricing has not reached an invoice. A confirmed dossier whose orders are still
                // drafts (e.g. a historically confirmed dossier) is not ready, so it is not listed.
                "readytoinvoice" => WhereDossierIn(dossiers,
                    DossierIdsWithOrder(tenantId, o => o.Status == TransportOrderStatus.Completed)
                        .Concat(_dbContext.DossierActivities
                            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == null
                                && a.ExecutionStatus == DossierActivityExecutionStatus.Completed
                                && _dbContext.ActivityTypes.Any(t => t.Id == a.ActivityTypeId && t.IsBillable)
                                && !_dbContext.InvoiceLines.Any(l => l.DossierActivityId == a.Id
                                    && _dbContext.Invoices.Any(i => i.Id == l.InvoiceId && i.Status != InvoiceStatus.Cancelled)))
                            .Select(a => a.DossierId))),
                // The rollup of the list row: Paid beats Sent beats Draft; a cancelled invoice never counts.
                "notinvoiced" => WhereDossierNotIn(dossiers, DossierIdsWithInvoice(tenantId, null)),
                "draft" => WhereDossierNotIn(
                    WhereDossierIn(dossiers, DossierIdsWithInvoice(tenantId, i => i.Status == InvoiceStatus.Draft)),
                    DossierIdsWithInvoice(tenantId, i => i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid)),
                "sent" => WhereDossierNotIn(
                    WhereDossierIn(dossiers, DossierIdsWithInvoice(tenantId, i => i.Status == InvoiceStatus.Sent)),
                    DossierIdsWithInvoice(tenantId, i => i.Status == InvoiceStatus.Paid)),
                "paid" => WhereDossierIn(dossiers, DossierIdsWithInvoice(tenantId, i => i.Status == InvoiceStatus.Paid)),
                _ => throw new DomainValidationException("invoiceStatus", "Onbekende factuurstatus."),
            };
        }

        if (query.HasCmr is { } hasCmr)
        {
            // A CMR reaches a dossier as an uploaded order document (on the dossier, or on an order
            // linked through dossier_orders when the document names no dossier) or as an issued CMR.
            var cmrOrderIds = _dbContext.TransportOrderDocuments
                .Where(doc => doc.TenantId == tenantId && doc.DocumentType == TransportOrderDocumentType.Cmr && doc.DossierId == null && doc.TransportOrderId != null)
                .Select(doc => doc.TransportOrderId!.Value);
            var cmrDossierIds = _dbContext.TransportOrderDocuments
                .Where(doc => doc.TenantId == tenantId && doc.DocumentType == TransportOrderDocumentType.Cmr && doc.DossierId != null)
                .Select(doc => doc.DossierId!.Value)
                .Concat(_dbContext.DossierOrders
                    .Where(l => l.TenantId == tenantId && cmrOrderIds.Contains(l.TransportOrderId))
                    .Select(l => l.DossierId))
                .Concat(_dbContext.IssuedTransportDocuments
                    .Where(doc => doc.TenantId == tenantId && doc.Kind == IssuedTransportDocumentKind.Cmr && doc.DossierId != null)
                    .Select(doc => doc.DossierId!.Value));
            dossiers = hasCmr ? WhereDossierIn(dossiers, cmrDossierIds) : WhereDossierNotIn(dossiers, cmrDossierIds);
        }

        // ---- global search: direct columns OR the customer OR one unioned id set (orders by
        // number/reference, stops by city/postal code, live trips by plate or driver name). Each id
        // subquery is uncorrelated, so inside this OR the planner evaluates it once (hashed SubPlan).
        if (Term(query.Search) is { } term)
        {
            var customerIds = _dbContext.Customers
                .Where(c => c.TenantId == tenantId && (c.Name.ToLower().Contains(term) || c.CustomerNumber.ToLower().Contains(term)))
                .Select(c => c.Id);
            var relatedDossierIds = DossierIdsWithOrder(tenantId, o => o.OrderNumber.ToLower().Contains(term) || (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term)))
                .Concat(DossierIdsWithStop(tenantId, s => (s.City != null && s.City.ToLower().Contains(term)) || (s.PostalCode != null && s.PostalCode.ToLower().StartsWith(term))))
                .Concat(DossierIdsWithTrip(tenantId, t =>
                    _dbContext.Vehicles.Any(v => v.Id == t.VehicleId && v.LicensePlate.ToLower().Contains(term))
                    || _dbContext.Drivers.Any(dr => dr.Id == t.DriverId
                        && _dbContext.Employees.Any(e => e.Id == dr.EmployeeId && (e.FirstName + " " + e.LastName).ToLower().Contains(term)))));
            dossiers = dossiers.Where(d =>
                d.DossierNumber.ToLower().Contains(term)
                || d.Title.ToLower().Contains(term)
                || (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(term))
                || (d.CustomerId != null && customerIds.Contains(d.CustomerId.Value))
                || relatedDossierIds.Contains(d.Id));
        }

        return dossiers;
    }

    private static string? Term(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static void ValidateRange(DateOnly? from, DateOnly? to, string field)
    {
        if (from is { } f && to is { } t && f > t)
        {
            throw new DomainValidationException(field, "De begindatum ligt na de einddatum.");
        }
    }

    // ------------------------------------------------------------ uncorrelated id-set builders
    //
    // Every builder yields IQueryable<Guid> of dossier ids and never references the outer dossier
    // row, so `d.Id IN (…)` is evaluated once per statement instead of once per dossier. The two
    // link paths (dossier_orders, activity-linked orders) are UNION ALL-ed: duplicates are harmless
    // inside IN and skipping the dedupe keeps the subquery an append of two index-able scans.

    private static IQueryable<TransportDossier> WhereDossierIn(IQueryable<TransportDossier> dossiers, IQueryable<Guid> dossierIds) =>
        dossiers.Where(d => dossierIds.Contains(d.Id));

    private static IQueryable<TransportDossier> WhereDossierNotIn(IQueryable<TransportDossier> dossiers, IQueryable<Guid> dossierIds) =>
        dossiers.Where(d => !dossierIds.Contains(d.Id));

    /// <summary>Dossiers linked (either path) to one of <paramref name="orderIds"/>.</summary>
    private IQueryable<Guid> DossierIdsOfOrders(Guid tenantId, IQueryable<Guid> orderIds) =>
        _dbContext.DossierOrders
            .Where(l => l.TenantId == tenantId && orderIds.Contains(l.TransportOrderId))
            .Select(l => l.DossierId)
            .Concat(_dbContext.DossierActivities
                .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId != null && orderIds.Contains(a.LinkedTransportOrderId!.Value))
                .Select(a => a.DossierId));

    /// <summary>Dossiers with a linked order matching <paramref name="predicate"/>.</summary>
    private IQueryable<Guid> DossierIdsWithOrder(Guid tenantId, Expression<Func<TransportOrder, bool>> predicate) =>
        DossierIdsOfOrders(tenantId, _dbContext.TransportOrders.Where(o => o.TenantId == tenantId).Where(predicate).Select(o => o.Id));

    /// <summary>Dossiers with a linked order that has a stop matching <paramref name="predicate"/>.</summary>
    private IQueryable<Guid> DossierIdsWithStop(Guid tenantId, Expression<Func<TransportOrderStop, bool>> predicate) =>
        DossierIdsOfOrders(tenantId, _dbContext.TransportOrderStops.Where(s => s.TenantId == tenantId).Where(predicate).Select(s => s.TransportOrderId));

    /// <summary>Dossiers with a linked order on a NON-cancelled trip matching <paramref name="predicate"/>.</summary>
    private IQueryable<Guid> DossierIdsWithTrip(Guid tenantId, Expression<Func<Trip, bool>> predicate)
    {
        var tripIds = _dbContext.Trips.Where(t => t.TenantId == tenantId && t.Status != TripStatus.Cancelled).Where(predicate).Select(t => t.Id);
        var orderIds = _dbContext.TripOrders.Where(to => to.TenantId == tenantId && tripIds.Contains(to.TripId)).Select(to => to.TransportOrderId);
        return DossierIdsOfOrders(tenantId, orderIds);
    }

    /// <summary>
    /// Dossiers with a line on a non-cancelled invoice matching <paramref name="predicate"/> (null =
    /// any), the line pointing at a linked order (either path) or at one of the dossier's activities.
    /// </summary>
    private IQueryable<Guid> DossierIdsWithInvoice(Guid tenantId, Expression<Func<Invoice, bool>>? predicate)
    {
        var invoices = _dbContext.Invoices.Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Cancelled);
        if (predicate is not null) invoices = invoices.Where(predicate);
        var invoiceIds = invoices.Select(i => i.Id);
        var lines = _dbContext.InvoiceLines.Where(l => l.TenantId == tenantId && invoiceIds.Contains(l.InvoiceId));
        var orderIds = lines.Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value);
        var activityIds = lines.Where(l => l.DossierActivityId != null).Select(l => l.DossierActivityId!.Value);
        return DossierIdsOfOrders(tenantId, orderIds)
            .Concat(_dbContext.DossierActivities
                .Where(a => a.TenantId == tenantId && activityIds.Contains(a.Id))
                .Select(a => a.DossierId));
    }

    // ------------------------------------------------------------ planning date (one derived set)

    private sealed class PlanningRow
    {
        public Guid DossierId { get; init; }
        public DateOnly PlannedOn { get; init; }
    }

    private sealed class DossierPlanningDate
    {
        public Guid DossierId { get; init; }
        public DateOnly PlanningDate { get; init; }
    }

    /// <summary>
    /// Every planning fact of the tenant as (dossier, date): the TripDate of each non-cancelled trip
    /// reached through dossier_orders or an activity-linked order, plus the PlannedDate of each
    /// standalone activity. Ungrouped so range filters see every date; grouped by
    /// <see cref="PlanningDatesByDossier"/> for the sort key (the same MIN the list row shows).
    /// </summary>
    private IQueryable<PlanningRow> PlanningRows(Guid tenantId)
    {
        var liveTrips = _dbContext.Trips.Where(t => t.TenantId == tenantId && t.Status != TripStatus.Cancelled);
        var tripOrders = _dbContext.TripOrders.Where(to => to.TenantId == tenantId);
        var viaOrderLinks = _dbContext.DossierOrders.Where(l => l.TenantId == tenantId)
            .Join(tripOrders, l => l.TransportOrderId, to => to.TransportOrderId, (l, to) => new { l.DossierId, to.TripId })
            .Join(liveTrips, x => x.TripId, t => t.Id, (x, t) => new PlanningRow { DossierId = x.DossierId, PlannedOn = t.TripDate });
        var viaActivityLinks = _dbContext.DossierActivities.Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId != null)
            .Join(tripOrders, a => a.LinkedTransportOrderId!.Value, to => to.TransportOrderId, (a, to) => new { a.DossierId, to.TripId })
            .Join(liveTrips, x => x.TripId, t => t.Id, (x, t) => new PlanningRow { DossierId = x.DossierId, PlannedOn = t.TripDate });
        var standalone = _dbContext.DossierActivities
            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == null && a.PlannedDate != null)
            .Select(a => new PlanningRow { DossierId = a.DossierId, PlannedOn = a.PlannedDate!.Value });
        return viaOrderLinks.Concat(viaActivityLinks).Concat(standalone);
    }

    private IQueryable<DossierPlanningDate> PlanningDatesByDossier(Guid tenantId) =>
        PlanningRows(tenantId)
            .GroupBy(r => r.DossierId)
            .Select(g => new DossierPlanningDate { DossierId = g.Key, PlanningDate = g.Min(r => r.PlannedOn) });

    /// <summary>Whitelisted sort keys; a stable DossierNumber secondary key keeps pagination consistent.</summary>
    private IQueryable<TransportDossier> ApplySort(IQueryable<TransportDossier> query, string? sort, string? dir, Guid tenantId)
    {
        var key = (sort ?? "date").Trim().ToLowerInvariant();
        if (!SortKeys.Contains(key)) key = "date";
        var descending = string.IsNullOrWhiteSpace(dir)
            ? key is "date" or "confirmedat" or "planningdate" or "createdat"
            : string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        if (key == "planningdate")
        {
            // LEFT JOIN the grouped planning set: the key is a column, nulls last in both directions,
            // and the secondary keys are the same as every other sort.
            var planning = PlanningDatesByDossier(tenantId);
            var joined = query.LeftJoin(planning, d => d.Id, p => p.DossierId,
                (d, p) => new { Dossier = d, PlanningDate = (DateOnly?)p!.PlanningDate });
            var orderedJoined = descending
                ? joined.OrderBy(x => x.PlanningDate == null).ThenByDescending(x => x.PlanningDate)
                : joined.OrderBy(x => x.PlanningDate == null).ThenBy(x => x.PlanningDate);
            return orderedJoined.ThenBy(x => x.Dossier.DossierNumber).ThenBy(x => x.Dossier.Id).Select(x => x.Dossier);
        }

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
