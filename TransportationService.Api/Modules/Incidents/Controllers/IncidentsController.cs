using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Controllers;

[ApiController]
[Route("api/incidents")]
public class IncidentsController : ControllerBase
{
    private readonly IIncidentService _service;
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IProblemFollowUpService _followUp;
    private readonly Modules.Identity.Services.ICurrentUserContext _currentUser;

    public IncidentsController(
        IIncidentService service, TransportationDbContext dbContext,
        ITenantContext tenantContext, IAuditService auditService,
        IProblemFollowUpService followUp, Modules.Identity.Services.ICurrentUserContext currentUser)
    {
        _service = service;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _followUp = followUp;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The incident register, paged and sorted server-side (PERF-H3). <c>page</c>/<c>pageSize</c>
    /// follow <see cref="Common.Models.PageRequest"/> (default 25, max 200); <c>sort</c> is one of
    /// created | title | severity | customer | due | status, <c>dir</c> asc | desc. Every filter of
    /// the register (openOnly, mine/responsibleUserId, driver, vehicle, period, customer, order,
    /// trip, dossier, search) is unchanged.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<Common.Models.PagedResult<IncidentListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? severity,
        [FromQuery] Guid? dossierId, [FromQuery] Guid? customerId,
        [FromQuery] Guid? transportOrderId, [FromQuery] Guid? tripId,
        [FromQuery] Guid? responsibleUserId, [FromQuery] bool? mine, [FromQuery] Guid? driverId, [FromQuery] Guid? vehicleId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool? openOnly,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? dir,
        CancellationToken cancellationToken)
    {
        var responsible = mine == true ? _currentUser.CurrentUserId : responsibleUserId;
        var filter = new IncidentListFilter(responsible, driverId, vehicleId, from, to, openOnly == true);
        var paging = IncidentListPage.Of(page, pageSize, sort, dir);
        return Ok(await _service.ListAsync(search, status, severity, dossierId, customerId, cancellationToken, transportOrderId, tripId, filter, paging));
    }

    /// <summary>EX-07: the responsible picker — safe fields only, no general users.view needed.</summary>
    [HttpGet("assignable-users")]
    [RequirePermission(PermissionCodes.IncidentsManage, PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<IReadOnlyList<AssignableUserDto>>> AssignableUsers(CancellationToken cancellationToken) =>
        Ok(await _followUp.AssignableUsersAsync(cancellationToken));

    /// <summary>Domain history (when / who / what) for everyone who may see the incident.</summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<Modules.Auditing.Dtos.AuditLogPagedResult>> History(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var history = await _followUp.HistoryAsync("Incident", id, page, pageSize, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    /// <summary>EX-03: create the linked fleet damage report (controlled: vehicle, trip and driver come from the
    /// incident). Problem handlers only — the response is the full incident, which the fleet may not read;
    /// the fleet creates its own reports on the vehicle and sees the linked ones there.</summary>
    [HttpPost("{id:guid}/damage-report")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> CreateDamageReport(
        Guid id, CreateLinkedDamageReportRequest request, CancellationToken cancellationToken)
    {
        var incident = await _followUp.CreateDamageReportForIncidentAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    /// <summary>EX-03: the open problems of a vehicle, for the fleet (no customer, cost or HR data).</summary>
    [HttpGet("/api/vehicles/{vehicleId:guid}/safety-issues")]
    [RequirePermission(PermissionCodes.DamageReportsView, PermissionCodes.IncidentsView)]
    public async Task<ActionResult<IReadOnlyList<VehicleSafetyIssueDto>>> VehicleSafetyIssues(Guid vehicleId, CancellationToken cancellationToken) =>
        Ok(await _followUp.VehicleSafetyIssuesAsync(vehicleId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Create(SaveIncidentRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _service.GetAsync(id, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Update(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.UpdateAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> ChangeStatus(Guid id, ChangeIncidentStatusRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.ChangeStatusAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    public record OrderDeliveryContextDto(
        string? DeliveryOutcome,
        Guid? RedeliveryOrderId, string? RedeliveryOrderNumber,
        Guid? RedeliveryOfOrderId, string? RedeliveryOfOrderNumber,
        Guid? SourceIncidentId, string? SourceIncidentTitle);

    /// <summary>EX-08 + redelivery links for the order page: what reached the consignee, and the
    /// redelivery relation in both directions (forward to the new order, back to the original).</summary>
    [HttpGet("/api/transport-orders/{orderId:guid}/delivery-context")]
    [RequirePermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<OrderDeliveryContextDto>> DeliveryContext(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .Select(o => new { o.Id, o.RedeliveryOfOrderId, o.RedeliverySourceIncidentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var outcome = (await Modules.Orders.Services.DeliveryOutcomeRules.ResolveAsync(_dbContext, tenantId, [orderId], cancellationToken))
            .GetValueOrDefault(orderId);
        var forward = outcome?.RedeliveryOrderId is null
            ? await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.RedeliveryOfOrderId == orderId && o.Status != Modules.Orders.Entities.TransportOrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new { o.Id, o.OrderNumber })
                .FirstOrDefaultAsync(cancellationToken)
            : new { Id = outcome.RedeliveryOrderId.Value, OrderNumber = outcome.RedeliveryOrderNumber! };
        var backNumber = order.RedeliveryOfOrderId is { } back
            ? await _dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId && o.Id == back)
                .Select(o => o.OrderNumber).FirstOrDefaultAsync(cancellationToken)
            : null;
        var incidentTitle = order.RedeliverySourceIncidentId is { } incidentId
            ? await _dbContext.Incidents.AsNoTracking().Where(i => i.TenantId == tenantId && i.Id == incidentId)
                .Select(i => i.Title).FirstOrDefaultAsync(cancellationToken)
            : null;
        return Ok(new OrderDeliveryContextDto(
            outcome?.Outcome, forward?.Id, forward?.OrderNumber,
            order.RedeliveryOfOrderId, backNumber, order.RedeliverySourceIncidentId, incidentTitle));
    }

    // --- Wave 6 §4: unified problem view ---

    [HttpGet("/api/problems")]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IReadOnlyList<ProblemListItemDto>>> ListProblems(CancellationToken cancellationToken)
        => Ok(await _service.ListProblemsAsync(cancellationToken));

    // --- Wave 6: charge decision + linked redelivery ---

    [HttpPost("{id:guid}/charge/propose")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> ProposeCharge(
        Guid id, ProposeIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.ProposeChargeAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    // EX-06: the decision is gated by the decision right itself — Boekhouding approves charges
    // without holding incidents.manage. The SERVICE still enforces problems.approve_charge
    // fail-closed (registered in Phase8SupplyChainTests), mirroring the L7 override pattern.
    [HttpPost("{id:guid}/charge/decide")]
    [RequirePermission(PermissionCodes.ProblemsApproveCharge)]
    public async Task<ActionResult<IncidentDetailDto>> DecideCharge(
        Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.DecideChargeAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    // The dispatcher creates redeliveries through this controlled command (problems.create_redelivery)
    // without general order-creation rights.
    [HttpPost("{id:guid}/redelivery")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage, PermissionCodes.ProblemsCreateRedelivery)]
    public async Task<ActionResult<IncidentDetailDto>> CreateRedelivery(
        Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateRedeliveryRequest? request,
        CancellationToken cancellationToken)
    {
        var incident = await _service.CreateRedeliveryAsync(id, cancellationToken, request);
        return incident is null ? NotFound() : Ok(incident);
    }

    // --- P5: configurable charge-decision policies ---

    public record ChargePolicyInput(
        Guid? CustomerId, string? IncidentType, string Mode, decimal? DefaultAmount, string? DefaultDescription);

    public record ChargePolicyDto(
        Guid Id, Guid? CustomerId, string? CustomerName, string? IncidentType,
        string Mode, decimal? DefaultAmount, string? DefaultDescription);

    [HttpGet("/api/settings/charge-policies")]
    [RequirePermission(PermissionCodes.ProblemsApproveCharge, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IReadOnlyList<ChargePolicyDto>>> ChargePolicies(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var policies = await _dbContext.IncidentChargePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.CustomerId == null).ThenBy(p => p.IncidentType == null)
            .ToListAsync(cancellationToken);
        var customerIds = policies.Where(p => p.CustomerId is not null).Select(p => p.CustomerId!.Value).Distinct().ToList();
        var names = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return Ok(policies.Select(p => new ChargePolicyDto(
            p.Id, p.CustomerId, p.CustomerId is { } cid ? names.GetValueOrDefault(cid) : null,
            p.IncidentType, p.Mode, p.DefaultAmount, p.DefaultDescription)).ToList());
    }

    /// <summary>Replace-all save; only charge approvers may shape the charging policy.</summary>
    [HttpPut("/api/settings/charge-policies")]
    [RequirePermission(PermissionCodes.ProblemsApproveCharge)]
    public async Task<ActionResult<IReadOnlyList<ChargePolicyDto>>> SaveChargePolicies(
        IReadOnlyList<ChargePolicyInput> request, CancellationToken cancellationToken)
    {
        foreach (var input in request)
        {
            if (input.Mode is not ("Never" or "Propose" or "Auto"))
            {
                throw new DomainValidationException("mode", "Ongeldige modus. Toegestaan: Never, Propose of Auto.");
            }

            if (input.Mode == "Auto" && input.DefaultAmount is not > 0m)
            {
                throw new DomainValidationException("defaultAmount",
                    "Automatisch doorrekenen vereist een positief standaardbedrag.");
            }
        }

        var tenantId = _tenantContext.TenantId;
        var existing = await _dbContext.IncidentChargePolicies
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        foreach (var policy in existing)
        {
            policy.IsDeleted = true;
        }

        foreach (var input in request)
        {
            _dbContext.IncidentChargePolicies.Add(new IncidentChargePolicy
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                CustomerId = input.CustomerId, IncidentType = input.IncidentType,
                Mode = input.Mode, DefaultAmount = input.DefaultAmount,
                DefaultDescription = input.DefaultDescription,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("IncidentChargePolicies", tenantId.ToString(), "PoliciesReplaced",
            new { Count = existing.Count }, new { Count = request.Count }, cancellationToken);
        return await ChargePolicies(cancellationToken);
    }
}
