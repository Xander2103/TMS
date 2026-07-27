using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Employees.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;
    private readonly IEmployeeHistoryService _historyService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public EmployeesController(
        IEmployeeService employeeService,
        IEmployeeHistoryService historyService,
        ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization)
    {
        _employeeService = employeeService;
        _historyService = historyService;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    /// <summary>Confidential fields (NRN/IBAN/BIC) are gated on employees.view_confidential.</summary>
    private async Task<bool> HasConfidentialAccessAsync(CancellationToken cancellationToken)
        => _currentUser.CurrentUserId is { } userId
           && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.EmployeesViewConfidential, cancellationToken);

    [HttpGet]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<PagedResult<EmployeeListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? jobFunctionId,
        [FromQuery] Guid? departmentId,
        [FromQuery] EmploymentStatus? employmentStatus,
        [FromQuery] bool excludeDrivers = false,
        [FromQuery] bool? hasDriverProfile = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _employeeService.SearchAsync(
            search, isActive, jobFunctionId, departmentId, employmentStatus, excludeDrivers, hasDriverProfile,
            PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<EmployeeDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.GetByIdAsync(id, await HasConfidentialAccessAsync(cancellationToken), cancellationToken);
        return employee is null ? NotFound() : Ok(employee);
    }

    /// <summary>
    /// Complete readable change history of the personnel dossier (profile + qualifications,
    /// documents, issued items, absences, leave balances, driver profile), newest first.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<EmployeeHistoryPageDto>> GetHistory(
        Guid id, [FromQuery] int? page = null, [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var history = await _historyService.GetHistoryAsync(id, pageRequest.Page, pageRequest.PageSize, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EmployeesCreate)]
    public async Task<ActionResult<EmployeeDetailDto>> Create(CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        // Inline qualifications ride the same permission as the qualifications tab.
        if (request.Qualifications is { Count: > 0 }
            && (_currentUser.CurrentUserId is not { } userId
                || !await _authorization.UserHasPermissionAsync(userId, PermissionCodes.EmployeeDocumentsCreate, cancellationToken)))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Je hebt geen rechten om kwalificaties toe te voegen." });
        }

        try
        {
            var created = await _employeeService.CreateAsync(request, await HasConfidentialAccessAsync(cancellationToken), cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Er bestaat al een medewerker met dit personeelsnummer." });
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesEdit)]
    public async Task<ActionResult<EmployeeDetailDto>> Update(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var updated = await _employeeService.UpdateAsync(id, request, await HasConfidentialAccessAsync(cancellationToken), cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCodes.EmployeesDeactivate)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _employeeService.DeactivateAsync(id, cancellationToken);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/reactivate")]
    [RequirePermission(PermissionCodes.EmployeesDeactivate)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var reactivated = await _employeeService.ReactivateAsync(id, cancellationToken);
        return reactivated ? NoContent() : NotFound();
    }
}
