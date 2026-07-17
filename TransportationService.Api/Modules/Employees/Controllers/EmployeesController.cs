using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private const int DefaultPageSize = 25;

    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<EmployeePagedResult>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _employeeService.SearchAsync(search, isActive, page < 1 ? 1 : page, pageSize < 1 ? DefaultPageSize : pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<EmployeeDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.GetByIdAsync(id, cancellationToken);
        return employee is null ? NotFound() : Ok(employee);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EmployeesCreate)]
    public async Task<ActionResult<EmployeeDetailDto>> Create(CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _employeeService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DbUpdateException)
        {
            return Conflict("An employee with this employee number already exists.");
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeesEdit)]
    public async Task<ActionResult<EmployeeDetailDto>> Update(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var updated = await _employeeService.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCodes.EmployeesDeactivate)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _employeeService.DeactivateAsync(id, cancellationToken);
        return deactivated ? NoContent() : NotFound();
    }
}
