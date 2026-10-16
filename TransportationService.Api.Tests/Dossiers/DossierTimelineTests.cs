using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Controllers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// T-H6 (browser acceptance 2026-10-16): the dossier "Wijzigingshistoriek" reconstructs the
/// business story — order intake, pricing, planning, documents, execution, POD, afwijkingen,
/// incidents, lifecycle — in Dutch, with the right actor per row (Systeem for automatic events),
/// and it is readable with dossiers.view (planner/dispatcher) without the global audit right.
/// </summary>
public class DossierTimelineTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc);

    private sealed record Seeded(SqliteTestDbContext Db, Guid TenantId, Guid DossierId, Guid Planner, Guid Dispatcher, Guid DriverUser, Guid CustomerUser);

    private static async Task<Seeded> SeedGoldenFlowAsync()
    {
        var db = new SqliteTestDbContext();
        var c = db.Context;
        var tenantId = Guid.NewGuid();
        var planner = Guid.NewGuid();
        var dispatcher = Guid.NewGuid();
        var driverUser = Guid.NewGuid();
        var customerUser = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var draftOrderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var loadStop = Guid.NewGuid();
        var unloadStop = Guid.NewGuid();

        c.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = T0 });
        c.Users.Add(new User { Id = planner, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        c.Users.Add(new User { Id = dispatcher, TenantId = tenantId, Email = "dispatch@acme.be", FirstName = "Dirk", LastName = "Dispatcher", IsActive = true });
        c.Users.Add(new User { Id = driverUser, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Peeters", IsActive = true, EmployeeId = employeeId });
        c.Users.Add(new User { Id = customerUser, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Karel", LastName = "Klant", IsActive = true, CustomerId = customerId });
        c.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = T0, UpdatedAt = T0 });
        c.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        c.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-ABC-123", IsActive = true });
        c.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        c.TransportDossiers.Add(new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0050", Title = "Levering Gent", CustomerId = customerId, Status = DossierStatus.Closed, CreatedByUserId = customerUser });
        var transportType = new ActivityType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "DIRECT_TRANSPORT", Name = "Transport", IsActive = true, HasStops = true, PlanningRelevant = true, IsBillable = true };
        c.ActivityTypes.Add(transportType);
        c.TransportOrders.Add(new TransportOrder { Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0050", OrderDate = DateOnly.FromDateTime(T0), Status = TransportOrderStatus.Completed, GoodsDescription = "Paletten" });
        c.TransportOrders.Add(new TransportOrder { Id = draftOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0051", OrderDate = DateOnly.FromDateTime(T0), Status = TransportOrderStatus.Cancelled, CancellationReason = "Klant heeft de tweede rit geannuleerd", GoodsDescription = "Paletten" });
        c.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId });
        c.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = draftOrderId });
        c.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, ActivityTypeId = transportType.Id, Sequence = 1, LinkedTransportOrderId = orderId });
        c.TransportOrderStops.Add(new TransportOrderStop { Id = loadStop, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen", CountryCode = "BE" });
        c.TransportOrderStops.Add(new TransportOrderStop { Id = unloadStop, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent", CountryCode = "BE" });
        c.Trips.Add(new Trip { Id = tripId, TenantId = tenantId, TripNumber = "RIT-0023", TripDate = DateOnly.FromDateTime(T0), DriverId = driverId, VehicleId = vehicleId, Status = TripStatus.Completed });
        await c.SaveChangesAsync();

        var tripOrderId = Guid.NewGuid();
        c.TripOrders.Add(new TripOrder { Id = tripOrderId, TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1, CreatedByUserId = planner });
        var loadExecution = new StopExecution { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderStopId = loadStop, Status = StopExecutionStatus.Completed };
        var unloadExecution = new StopExecution { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderStopId = unloadStop, Status = StopExecutionStatus.Completed };
        c.StopExecutions.AddRange(loadExecution, unloadExecution);
        await c.SaveChangesAsync();

        // Intake: the customer submits via the portal, the planner accepts and prices.
        Audit(c, tenantId, customerUser, "TransportOrder", orderId, "Created", T0.AddMinutes(1), new { OrderNumber = "ORD-0050" });
        Audit(c, tenantId, customerUser, "TransportOrder", orderId, "PortalSubmitted", T0.AddMinutes(1).AddSeconds(1), new { OrderNumber = "ORD-0050" });
        History(c, tenantId, orderId, TransportOrderStatus.Draft, TransportOrderStatus.Submitted, customerUser, T0.AddMinutes(1).AddSeconds(1), "Ingediend via het klantportaal");
        History(c, tenantId, orderId, TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, planner, T0.AddMinutes(30));
        Audit(c, tenantId, planner, "OrderPricing", orderId, "oneOffPriceSet", T0.AddMinutes(35), new { PricingSource = "OneOff", OneOffFixedAmount = 450m, AgreedPrice = 450m });

        // Planning: trip created with the order, then planned (order follows the trip).
        Audit(c, tenantId, planner, "Trip", tripId, "Created", T0.AddHours(1), new { TripNumber = "RIT-0023", TripDate = DateOnly.FromDateTime(T0), DriverId = driverId, VehicleId = vehicleId });
        Audit(c, tenantId, planner, "Trip", tripId, "StatusChanged", T0.AddHours(1).AddMinutes(5), new { Status = "Planned", Overridden = false, OverrideReason = (string?)null }, new { Status = "Draft" });
        History(c, tenantId, orderId, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned, planner, T0.AddHours(1).AddMinutes(5));

        // Document: CMR issued.
        c.IssuedTransportDocuments.Add(new IssuedTransportDocument { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, DossierId = dossierId, Kind = IssuedTransportDocumentKind.Cmr, DocumentNumber = "CMR-2026-00012", RequestId = Guid.NewGuid(), IssuedAt = T0.AddHours(1).AddMinutes(10), CreatedByUserId = planner });

        // Execution: dispatcher starts, the driver completes the stops, reports damage, registers POD.
        Audit(c, tenantId, dispatcher, "Trip", tripId, "StatusChanged", T0.AddHours(2), new { Status = "InProgress", Overridden = false }, new { Status = "Planned" });
        History(c, tenantId, orderId, TransportOrderStatus.Planned, TransportOrderStatus.InProgress, dispatcher, T0.AddHours(2));
        StopStep(c, tenantId, loadExecution.Id, StopExecutionStatus.Planned, StopExecutionStatus.Arrived, driverUser, T0.AddHours(3));
        StopStep(c, tenantId, loadExecution.Id, StopExecutionStatus.Arrived, StopExecutionStatus.Completed, driverUser, T0.AddHours(3).AddMinutes(20));
        var exceptionId = Guid.NewGuid();
        c.ExecutionExceptions.Add(new ExecutionException { Id = exceptionId, TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, TransportOrderStopId = unloadStop, Type = ExecutionExceptionType.DamagedPackage, Severity = ExceptionSeverity.High, Description = "Pallet 3 beschadigd bij het lossen", ReportedByUserId = driverUser, OccurredAt = T0.AddHours(5) });
        var podId = Guid.NewGuid();
        c.ProofsOfDelivery.Add(new ProofOfDelivery { Id = podId, TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, TransportOrderStopId = unloadStop, RecipientName = "M. Janssens", Outcome = PodOutcome.Partial, DamageReported = true, DeliveredAt = T0.AddHours(5).AddMinutes(10), FinalisedByUserId = driverUser });
        StopStep(c, tenantId, unloadExecution.Id, StopExecutionStatus.Arrived, StopExecutionStatus.Completed, driverUser, T0.AddHours(5).AddMinutes(15));

        // The last stop closes the trip, the trip completes the order, the dossier confirms itself —
        // all inside the driver's request, so every audit row carries the DRIVER's user id.
        var closing = T0.AddHours(5).AddMinutes(15).AddMilliseconds(200);
        Audit(c, tenantId, driverUser, "Trip", tripId, "StatusChanged", closing, new { Status = "Completed", Overridden = false }, new { Status = "InProgress" });
        History(c, tenantId, orderId, TransportOrderStatus.InProgress, TransportOrderStatus.Completed, driverUser, closing);
        Audit(c, tenantId, driverUser, "TransportDossier", dossierId, "ConfirmedAutomatically", closing.AddMilliseconds(50),
            new { DossierNumber = "DOS-0050", ConfirmedAt = closing, Trigger = "trip:RIT-0023", CompletedOrders = 1, ExecutedActivities = 0 });

        // Follow-up: incident with redelivery, reopen + manual confirmation, the second order cancelled.
        var incidentId = Guid.NewGuid();
        c.Incidents.Add(new Incident { Id = incidentId, TenantId = tenantId, Title = "Schade pallet 3", Description = "Kras", IncidentType = IncidentType.Damage, Severity = IncidentSeverity.Medium, DossierId = dossierId, TransportOrderId = orderId, Status = IncidentStatus.Resolved });
        Audit(c, tenantId, dispatcher, "Incident", incidentId, "Created", T0.AddHours(6), new { Title = "Schade pallet 3", IncidentType = "Damage", Severity = "Medium" });
        Audit(c, tenantId, dispatcher, "Incident", incidentId, "RedeliveryCreated", T0.AddHours(6).AddMinutes(5), new { RedeliveryOrderId = Guid.NewGuid(), OrderNumber = "ORD-0052" });
        Audit(c, tenantId, dispatcher, "Incident", incidentId, "StatusChanged", T0.AddHours(6).AddMinutes(10), new { Status = "Resolved", Resolution = "Herlevering ingepland" }, new { Status = "New" });
        Audit(c, tenantId, planner, "TransportDossier", dossierId, "Reopened", T0.AddHours(7),
            new { DossierNumber = "DOS-0050", Reason = "Herlevering toevoegen", ReopenedByUserId = planner, ReopenedAt = T0.AddHours(7) },
            new { Status = "Closed", ClosedAt = closing, ConfirmationSource = "Automatic", ConfirmedByUserId = (Guid?)null, ConfirmationReason = (string?)null });
        History(c, tenantId, draftOrderId, TransportOrderStatus.Draft, TransportOrderStatus.Cancelled, planner, T0.AddHours(7).AddMinutes(5));
        Audit(c, tenantId, planner, "TransportDossier", dossierId, "ConfirmedManually", T0.AddHours(8),
            new { DossierNumber = "DOS-0050", ConfirmedAt = T0.AddHours(8), ConfirmedByUserId = planner, Reason = "Nagekeken met de klant",
                  WarningsAcknowledged = new[] { "order.not_completed", "nothing.completed", "pricing.incomplete", "delivery.pod_missing" } });
        await c.SaveChangesAsync();

        // The interceptor stamps CreatedAt = now on insert; the story needs the real moments.
        await c.TransportDossiers.Where(d => d.Id == dossierId).ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAt, T0));
        await c.TransportOrders.Where(o => o.Id == draftOrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, T0.AddMinutes(40)).SetProperty(o => o.CreatedByUserId, planner));
        await c.TripOrders.Where(l => l.Id == tripOrderId).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, T0.AddHours(1)));
        await c.ProofsOfDelivery.Where(p => p.Id == podId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, T0.AddHours(5).AddMinutes(10)));
        await c.Incidents.Where(i => i.Id == incidentId).ExecuteUpdateAsync(s => s.SetProperty(i => i.CreatedAt, T0.AddHours(6)));

        return new Seeded(db, tenantId, dossierId, planner, dispatcher, driverUser, customerUser);
    }

    private static readonly JsonSerializerOptions EnumNames = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static void Audit(TransportationDbContext c, Guid tenantId, Guid? userId, string entityType, Guid entityId, string action, DateTime at, object? after, object? before = null) =>
        c.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, EntityType = entityType, EntityId = entityId.ToString(), Action = action, Timestamp = at,
            NewValuesJson = after is null ? null : JsonSerializer.Serialize(after, EnumNames),
            OldValuesJson = before is null ? null : JsonSerializer.Serialize(before, EnumNames),
        });

    private static void History(TransportationDbContext c, Guid tenantId, Guid orderId, TransportOrderStatus from, TransportOrderStatus to, Guid? userId, DateTime at, string? reason = null) =>
        c.TransportOrderStatusHistories.Add(new TransportOrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId, FromStatus = from, ToStatus = to, ChangedByUserId = userId, ChangedAt = at, Reason = reason });

    private static void StopStep(TransportationDbContext c, Guid tenantId, Guid executionId, StopExecutionStatus from, StopExecutionStatus to, Guid userId, DateTime at) =>
        c.StopStatusHistories.Add(new StopStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, StopExecutionId = executionId, FromStatus = from, ToStatus = to, UserId = userId, OccurredAt = at });

    private static async Task<IReadOnlyList<DossierTimelineEventDto>> TimelineAsync(Seeded s) =>
        (await new DossierTimelineService(s.Db.Context, new DevTenantContext(s.TenantId)).GetAsync(s.DossierId, CancellationToken.None))!;

    [Fact]
    public async Task GoldenFlow_ReadsAsOneChronologicalDutchStory_WithTheRightActors()
    {
        var s = await SeedGoldenFlowAsync();
        using var _ = s.Db;

        var timeline = await TimelineAsync(s);
        var rows = timeline.Select(e => $"{e.ActorName ?? e.ActorKind} · {e.Title}").ToList();

        Assert.Equal(
            new[]
            {
                "Karel Klant · Dossier DOS-0050 aangemaakt",
                "Karel Klant · Transportaanvraag ORD-0050 ingediend via het klantportaal",
                "Piet Planner · Transportaanvraag ORD-0050 aanvaard",
                "Piet Planner · Afgesproken prijs voor opdracht ORD-0050 vastgelegd: € 450,00",
                "Piet Planner · Transportopdracht ORD-0051 aangemaakt",
                "Piet Planner · Rit RIT-0023 aangemaakt voor opdracht ORD-0050",
                "Piet Planner · Rit RIT-0023 gepland met chauffeur Jan Peeters en voertuig VRT-0001 (1-ABC-123)",
                "Piet Planner · CMR CMR-2026-00012 uitgegeven voor opdracht ORD-0050",
                "Dirk Dispatcher · Rit RIT-0023 gestart",
                "Jan Peeters · Stop 1 (laden, Antwerpen) van opdracht ORD-0050 afgerond",
                "Jan Peeters · Afwijking gemeld: beschadigd collo bij stop 2 (lossen, Gent) van opdracht ORD-0050",
                "Jan Peeters · Afleverbewijs geregistreerd voor stop 2 (lossen, Gent) van opdracht ORD-0050",
                "Jan Peeters · Stop 2 (lossen, Gent) van opdracht ORD-0050 afgerond",
                "System · Rit RIT-0023 voltooid na de laatste stop",
                "System · Transportopdracht ORD-0050 voltooid na voltooiing van de rit",
                "System · Dossier automatisch bevestigd na voltooiing van rit RIT-0023",
                "Dirk Dispatcher · Incident 'Schade pallet 3' gemeld",
                "Dirk Dispatcher · Herlevering ORD-0052 aangemaakt voor incident 'Schade pallet 3'",
                "Dirk Dispatcher · Incident 'Schade pallet 3' opgelost",
                "Piet Planner · Dossier heropend",
                "Piet Planner · Transportopdracht ORD-0051 geannuleerd",
                "Piet Planner · Dossier handmatig bevestigd",
            },
            rows);

        // Automatic confirmation: the system, never the driver whose stop triggered it.
        var auto = timeline.Single(e => e.Type == "dossier.confirmed_automatically");
        Assert.Equal("System", auto.ActorKind);
        Assert.Null(auto.ActorName);
        Assert.Contains(auto.Fields, f => f.Label == "Aanleiding" && f.Value == "Laatste handeling door Jan Peeters");

        // Codes and English property names are translated, reasons travel as Dutch details.
        var manual = timeline.Single(e => e.Type == "dossier.confirmed_manually");
        Assert.Equal("Opmerking: Nagekeken met de klant", manual.Detail);
        Assert.Equal("Opdracht nog niet uitgevoerd; Nog niets uitgevoerd; Prijs onvolledig; Afleverbewijs (POD) ontbreekt",
            Assert.Single(manual.Fields, f => f.Label == "Bewust bevestigde aandachtspunten").Value);
        var reopened = timeline.Single(e => e.Type == "dossier.reopened");
        Assert.Equal("Reden: Herlevering toevoegen", reopened.Detail);
        var status = Assert.Single(reopened.Fields, f => f.Label == "Status");
        Assert.Equal(("Bevestigd", "Open"), (status.Before, status.Value));
        Assert.Contains(reopened.Fields, f => f.Label == "Eerdere bevestiging" && f.Value == "Automatisch");
        Assert.Equal("Reden: Klant heeft de tweede rit geannuleerd", timeline.Single(e => e.Type == "order.cancelled").Detail);
        Assert.Equal("Oplossing: Herlevering ingepland", timeline.Single(e => e.Type == "incident.resolved").Detail);
        var pod = timeline.Single(e => e.Type == "pod.registered");
        Assert.Contains(pod.Fields, f => f.Label == "Ontvanger" && f.Value == "M. Janssens");
        Assert.Contains(pod.Fields, f => f.Label == "Resultaat" && f.Value == "Gedeeltelijk geleverd");

        // Everything a user reads: titles, details, field labels and values.
        var everything = JsonSerializer.Serialize(timeline.Select(e => new { e.Title, e.Detail, e.ActorName, e.Fields }));
        foreach (var raw in new[] { "order.not_completed", "nothing.completed", "pricing.incomplete", "pod_missing", "trip:", "Closed", "ConfirmationReason", "CancelledByUserId", "ReopenedAt", "InProgress" })
        {
            Assert.DoesNotContain(raw, everything);
        }

        // Categories drive the filter chips.
        Assert.Equal("pricing", timeline.Single(e => e.Type == "order.price_set").Category);
        Assert.Equal("planning", timeline.Single(e => e.Type == "trip.planned").Category);
        Assert.Equal("documents", timeline.Single(e => e.Type == "document.issued").Category);
        Assert.Equal("documents", pod.Category);
        Assert.Equal("incidents", timeline.Single(e => e.Type == "exception.reported").Category);
        Assert.Equal("orders", timeline.Single(e => e.Type == "order.accepted").Category);
    }

    [Fact]
    public async Task ForeignTenant_SeesNoTimeline()
    {
        var s = await SeedGoldenFlowAsync();
        using var _ = s.Db;

        var foreign = await new DossierTimelineService(s.Db.Context, new DevTenantContext(Guid.NewGuid())).GetAsync(s.DossierId, CancellationToken.None);
        Assert.Null(foreign);
    }

    /// <summary>
    /// The real write path (TripService → DossierLifecycleService) inside a driver's request: the
    /// audit rows carry the driver's id, yet the timeline attributes the confirmation to Systeem and
    /// reads the trip's own payloads (driver and vehicle names).
    /// </summary>
    [Fact]
    public async Task RealTripCompletion_AutoConfirmationIsSystem_NotTheTriggeringUser()
    {
        var now = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        using var db = new SqliteTestDbContext();
        var c = db.Context;
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var driverUser = Guid.NewGuid();
        c.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = now.UtcDateTime });
        c.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 23, QualificationExpiryWarningDays = 30 });
        c.Users.Add(new User { Id = driverUser, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Peeters", IsActive = true });
        c.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime });
        c.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        c.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        c.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        c.TransportDossiers.Add(new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-1", Title = "Werf", CustomerId = customerId, Status = DossierStatus.Open });
        var transportType = new ActivityType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "DIRECT_TRANSPORT", Name = "Transport", IsActive = true, HasStops = true, PlanningRelevant = true, IsBillable = true };
        c.ActivityTypes.Add(transportType);
        c.TransportOrders.Add(new TransportOrder { Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-A", OrderDate = DateOnly.FromDateTime(now.UtcDateTime), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten" });
        c.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId });
        c.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, ActivityTypeId = transportType.Id, Sequence = 1, LinkedTransportOrderId = orderId });
        await c.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(now);
        var asDriver = new DevCurrentUserContext(driverUser);
        var audit = new AuditService(c, tenant, asDriver);
        var dossiers = new DossierService(c, tenant, audit, clock);
        var lifecycle = new DossierLifecycleService(c, tenant, audit, clock, asDriver, () => dossiers, new DossierReadinessService(c, tenant));
        var trips = new TripService(c, tenant, audit,
            new PlanningConflictService(c, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(c, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(c, tenant),
            CostingTestFactory.Create(c, tenant, clock),
            TripPackageTestFactory.Create(c, tenant, clock),
            dossierLifecycle: lifecycle);
        var created = await trips.CreateAsync(new CreateTripRequest(DateOnly.FromDateTime(now.UtcDateTime), driverId, vehicleId, null, null, null, null, [orderId]), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, created.Outcome);
        foreach (var target in new[] { TripStatus.Planned, TripStatus.InProgress, TripStatus.Completed })
        {
            Assert.Equal(TripOperationOutcome.Success, (await trips.ChangeStatusAsync(created.Trip!.Id, target, false, false, null, CancellationToken.None)).Outcome);
        }

        Assert.Equal(DossierStatus.Closed, (await c.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == dossierId)).Status);

        var timeline = (await new DossierTimelineService(c, tenant).GetAsync(dossierId, CancellationToken.None))!;
        var number = created.Trip!.TripNumber;
        var confirmation = timeline.Single(e => e.Type == "dossier.confirmed_automatically");
        Assert.Equal("System", confirmation.ActorKind);
        Assert.Equal($"Dossier automatisch bevestigd na voltooiing van rit {number}", confirmation.Title);
        Assert.Equal($"Rit {number} gepland met chauffeur Jan Peeters en voertuig VRT-0001 (1-A-1)", timeline.Single(e => e.Type == "trip.planned").Title);
        Assert.Equal($"Rit {number} aangemaakt voor opdracht ORD-A", timeline.Single(e => e.Type == "trip.created").Title);
        // The trip carried the order through planned/started: no separate order rows for those steps.
        Assert.DoesNotContain(timeline, e => e.Type is "order.planned" or "order.started");
        var completedOrder = timeline.Single(e => e.Type == "order.completed");
        Assert.Equal("System", completedOrder.ActorKind);
        // The confirmation is the last word of the story.
        Assert.Equal("dossier.confirmed_automatically", timeline[^1].Type);
    }

    // ------------------------------------------------------------ permissions

    private sealed class TemplatePermissions(IReadOnlyCollection<string> granted) : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(granted.Contains(permissionCode));
    }

    private static async Task<int> StatusForTemplateAsync(string templateCode)
    {
        var granted = TransportationService.Api.Data.DefaultRoleDefinitions.All.Single(t => t.Code == templateCode).PermissionCodes;
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUserContext>(new DevCurrentUserContext(Guid.NewGuid()))
            .AddSingleton<IPermissionAuthorizationService>(new TemplatePermissions(granted))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var executing = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(), controller: null!);
        var attribute = typeof(DossierTimelineController).GetMethod(nameof(DossierTimelineController.Get))!.GetCustomAttribute<RequirePermissionAttribute>()!;

        var reached = false;
        await attribute.OnActionExecutionAsync(executing, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(executing, [], controller: null!));
        });

        return reached ? StatusCodes.Status200OK : ((ObjectResult)executing.Result!).StatusCode!.Value;
    }

    [Theory]
    [InlineData("dispatcher", StatusCodes.Status200OK)]
    [InlineData("planner", StatusCodes.Status200OK)]
    [InlineData("chauffeur", StatusCodes.Status403Forbidden)]
    [InlineData("klantportaal", StatusCodes.Status403Forbidden)]
    public async Task Timeline_FollowsTheDossierReadRight(string template, int expected)
    {
        var granted = TransportationService.Api.Data.DefaultRoleDefinitions.All.Single(t => t.Code == template).PermissionCodes;
        Assert.Equal(expected == StatusCodes.Status200OK, granted.Contains(PermissionCodes.DossiersView) || granted.Contains(PermissionCodes.DossiersManage));
        Assert.Equal(expected, await StatusForTemplateAsync(template));
    }

    [Fact]
    public void Timeline_IsDossierScoped_NotTheGlobalAuditRight()
    {
        var codes = typeof(DossierTimelineController).GetMethod(nameof(DossierTimelineController.Get))!
            .GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;
        Assert.Equal(new[] { PermissionCodes.DossiersView, PermissionCodes.DossiersManage }, codes);
        Assert.DoesNotContain(PermissionCodes.AuditLogsView, codes);
        // The raw tenant audit log stays behind audit_logs.view — the dispatcher still lacks it.
        var dispatcher = TransportationService.Api.Data.DefaultRoleDefinitions.All.Single(t => t.Code == "dispatcher").PermissionCodes;
        Assert.DoesNotContain(PermissionCodes.AuditLogsView, dispatcher);
    }
}
