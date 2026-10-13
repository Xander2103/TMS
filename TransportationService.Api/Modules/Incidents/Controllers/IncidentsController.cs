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

    public IncidentsController(
        IIncidentService service, TransportationDbContext dbContext,
        ITenantContext tenantContext, IAuditService auditService)
    {
        _service = service;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IReadOnlyList<IncidentListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? severity,
        [FromQuery] Guid? dossierId, [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(search, status, severity, dossierId, customerId, cancellationToken));
    }

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
    public async Task<ActionResult<IncidentDetailDto>> Update(Guid id, SaveIncidentRequest request, CancellationToken cancellationToken)
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

    // The attribute gate stays IncidentsManage: the SERVICE enforces problems.approve_charge
    // fail-closed (registered in Phase8SupplyChainTests), mirroring the L7 override pattern.
    [HttpPost("{id:guid}/charge/decide")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> DecideCharge(
        Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.DecideChargeAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPost("{id:guid}/redelivery")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IncidentDetailDto>> CreateRedelivery(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _service.CreateRedeliveryAsync(id, cancellationToken);
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
