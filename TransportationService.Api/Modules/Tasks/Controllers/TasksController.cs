using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Security;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tasks.Services;

namespace TransportationService.Api.Modules.Tasks.Controllers;

/// <summary>
/// Employee tasks. The service scopes every read/mutation to the caller (own tasks,
/// department with tasks.view_team, everything with tasks.view_all); out-of-scope ids are 404.
/// </summary>
[ApiController]
public class TasksController : ControllerBase
{
    private const long MaxAttachmentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedAttachmentExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly IEmployeeTaskService _service;
    private readonly ITaskAttachmentService _attachments;

    public TasksController(IEmployeeTaskService service, ITaskAttachmentService attachments)
    {
        _service = service;
        _attachments = attachments;
    }

    [HttpGet("api/tasks")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<ActionResult<TaskListResultDto>> List([FromQuery] TaskListQuery query, CancellationToken cancellationToken) =>
        Ok(await _service.ListAsync(query, cancellationToken));

    [HttpPost("api/tasks")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksAssign)]
    public async Task<ActionResult<IReadOnlyList<EmployeeTaskDto>>> Create(CreateTaskRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.CreateAsync(request, cancellationToken));

    [HttpGet("api/tasks/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<ActionResult<EmployeeTaskDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await _service.GetAsync(id, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPut("api/tasks/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksAssign, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken) =>
        await _service.UpdateAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/start")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> Start(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.StartAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/block")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> Block(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.BlockAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/resume")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> Resume(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.ResumeAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/submit-for-review")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> SubmitForReview(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.SubmitForReviewAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/complete")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<ActionResult<EmployeeTaskDto>> Complete(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.CompleteAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/review")]
    [RequirePermission(PermissionCodes.TasksReview, PermissionCodes.TasksAssign)]
    public async Task<ActionResult<EmployeeTaskDto>> Review(Guid id, ReviewTaskRequest request, CancellationToken cancellationToken) =>
        await _service.ReviewAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/reopen")]
    [RequirePermission(PermissionCodes.TasksReopen)]
    public async Task<ActionResult<EmployeeTaskDto>> Reopen(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.ReopenAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpPost("api/tasks/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksCancel)]
    public async Task<ActionResult<EmployeeTaskDto>> Cancel(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        await _service.CancelAsync(id, request, cancellationToken) is { } task ? Ok(task) : NotFound();

    [HttpGet("api/employees/{employeeId:guid}/tasks")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<ActionResult<IReadOnlyList<EmployeeTaskDto>>> ForEmployee(Guid employeeId, CancellationToken cancellationToken) =>
        await _service.ListForEmployeeAsync(employeeId, cancellationToken) is { } tasks ? Ok(tasks) : NotFound();

    [HttpGet("api/employees/{employeeId:guid}/tasks/open-summary")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<ActionResult<EmployeeOpenTaskSummaryDto>> OpenSummary(Guid employeeId, CancellationToken cancellationToken) =>
        await _service.OpenSummaryAsync(employeeId, cancellationToken) is { } summary ? Ok(summary) : NotFound();

    [HttpPost("api/tasks/redistribute")]
    [RequirePermission(PermissionCodes.TasksAssign)]
    public async Task<ActionResult<RedistributeResultDto>> Redistribute(RedistributeTasksRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.RedistributeAsync(request, cancellationToken));

    // ------------------------------------------------------------- attachments (evidence)

    [HttpGet("api/tasks/{id:guid}/attachments")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<ActionResult<IReadOnlyList<TaskAttachmentDto>>> ListAttachments(Guid id, CancellationToken cancellationToken) =>
        await _attachments.ListAsync(id, cancellationToken) is { } list ? Ok(list) : NotFound();

    [HttpPost("api/tasks/{id:guid}/attachments")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    [RequestSizeLimit(MaxAttachmentBytes + 1024)]
    public async Task<ActionResult<TaskAttachmentDto>> Upload(
        Guid id, IFormFile file, [FromForm] string? note, CancellationToken cancellationToken)
    {
        if (UploadValidation.Validate(file, MaxAttachmentBytes, AllowedAttachmentExtensions) is { } error)
        {
            return BadRequest(new { message = error });
        }

        await using var stream = file.OpenReadStream();
        var attachment = await _attachments.UploadAsync(
            id, file.FileName, ContentTypeFor(file.FileName), file.Length, stream, note, cancellationToken);
        return attachment is null ? NotFound() : Ok(attachment);
    }

    [HttpGet("api/tasks/{id:guid}/attachments/{attachmentId:guid}/content")]
    [RequirePermission(PermissionCodes.TasksViewOwn, PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll)]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var content = await _attachments.DownloadAsync(id, attachmentId, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        // Content type derived server-side from the stored name; the client's value is never trusted.
        return File(content.Stream, ContentTypeFor(content.FileName), content.FileName);
    }

    [HttpDelete("api/tasks/{id:guid}/attachments/{attachmentId:guid}")]
    [RequirePermission(PermissionCodes.TasksManageOwn, PermissionCodes.TasksEdit)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken) =>
        await _attachments.DeleteAsync(id, attachmentId, cancellationToken) ? NoContent() : NotFound();

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}

[Route("api/task-categories")]
public class TaskCategoriesController : LookupControllerBase<TaskCategory>
{
    public TaskCategoriesController(ILookupService<TaskCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.TasksViewOwn;
    protected override string ManagePermission => PermissionCodes.TasksManageCategories;
}

[ApiController]
public class TaskTemplatesController : ControllerBase
{
    private readonly ITaskTemplateService _service;

    public TaskTemplatesController(ITaskTemplateService service)
    {
        _service = service;
    }

    [HttpGet("api/task-templates")]
    [RequirePermission(PermissionCodes.TasksManageTemplates, PermissionCodes.TasksAssign)]
    public async Task<ActionResult<IReadOnlyList<TaskTemplateDto>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await _service.ListTemplatesAsync(includeInactive, cancellationToken));

    [HttpPost("api/task-templates")]
    [RequirePermission(PermissionCodes.TasksManageTemplates)]
    public async Task<ActionResult<TaskTemplateDto>> Create(SaveTaskTemplateRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.CreateTemplateAsync(request, cancellationToken));

    [HttpPut("api/task-templates/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksManageTemplates)]
    public async Task<ActionResult<TaskTemplateDto>> Update(Guid id, SaveTaskTemplateRequest request, CancellationToken cancellationToken) =>
        await _service.UpdateTemplateAsync(id, request, cancellationToken) is { } template ? Ok(template) : NotFound();

    [HttpDelete("api/task-templates/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksManageTemplates)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteTemplateAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("api/task-templates/{id:guid}/apply")]
    [RequirePermission(PermissionCodes.TasksAssign)]
    public async Task<ActionResult<IReadOnlyList<EmployeeTaskDto>>> Apply(
        Guid id, ApplyTaskTemplateRequest request, CancellationToken cancellationToken) =>
        await _service.ApplyTemplateAsync(id, request, cancellationToken) is { } tasks ? Ok(tasks) : NotFound();

    [HttpGet("api/task-recurrences")]
    [RequirePermission(PermissionCodes.TasksManageRecurring, PermissionCodes.TasksManageTemplates)]
    public async Task<ActionResult<IReadOnlyList<TaskRecurrenceDto>>> ListRecurrences(CancellationToken cancellationToken) =>
        Ok(await _service.ListRecurrencesAsync(cancellationToken));

    [HttpPost("api/task-recurrences")]
    [RequirePermission(PermissionCodes.TasksManageRecurring)]
    public async Task<ActionResult<TaskRecurrenceDto>> CreateRecurrence(SaveTaskRecurrenceRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.CreateRecurrenceAsync(request, cancellationToken));

    [HttpPut("api/task-recurrences/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksManageRecurring)]
    public async Task<ActionResult<TaskRecurrenceDto>> UpdateRecurrence(Guid id, SaveTaskRecurrenceRequest request, CancellationToken cancellationToken) =>
        await _service.UpdateRecurrenceAsync(id, request, cancellationToken) is { } recurrence ? Ok(recurrence) : NotFound();

    [HttpDelete("api/task-recurrences/{id:guid}")]
    [RequirePermission(PermissionCodes.TasksManageRecurring)]
    public async Task<IActionResult> DeleteRecurrence(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteRecurrenceAsync(id, cancellationToken) ? NoContent() : NotFound();
}
