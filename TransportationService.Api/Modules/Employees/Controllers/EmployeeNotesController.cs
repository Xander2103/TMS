using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

[ApiController]
[Route("api/employees/{employeeId:guid}/notes")]
public class EmployeeNotesController : ControllerBase
{
    private readonly IEmployeeNoteService _service;

    public EmployeeNotesController(IEmployeeNoteService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.EmployeeNotesView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeNoteDto>>> List(Guid employeeId, CancellationToken cancellationToken)
    {
        var notes = await _service.ListAsync(employeeId, cancellationToken);
        return notes is null ? NotFound() : Ok(notes);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.EmployeeNotesManage)]
    public async Task<ActionResult<EmployeeNoteDto>> Create(Guid employeeId, CreateEmployeeNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await _service.CreateAsync(employeeId, request.Text, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{noteId:guid}")]
    [RequirePermission(PermissionCodes.EmployeeNotesManage)]
    public async Task<ActionResult<EmployeeNoteDto>> Update(Guid employeeId, Guid noteId, UpdateEmployeeNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await _service.UpdateAsync(employeeId, noteId, request.Text, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpDelete("{noteId:guid}")]
    [RequirePermission(PermissionCodes.EmployeeNotesManage)]
    public async Task<IActionResult> Delete(Guid employeeId, Guid noteId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(employeeId, noteId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{noteId:guid}/pin")]
    [RequirePermission(PermissionCodes.EmployeeNotesPin)]
    public async Task<ActionResult<EmployeeNoteDto>> Pin(Guid employeeId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _service.SetPinnedAsync(employeeId, noteId, true, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPost("{noteId:guid}/unpin")]
    [RequirePermission(PermissionCodes.EmployeeNotesPin)]
    public async Task<ActionResult<EmployeeNoteDto>> Unpin(Guid employeeId, Guid noteId, CancellationToken cancellationToken)
    {
        var note = await _service.SetPinnedAsync(employeeId, noteId, false, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }
}

public record CreateEmployeeNoteRequest(string Text);
public record UpdateEmployeeNoteRequest(string Text);
