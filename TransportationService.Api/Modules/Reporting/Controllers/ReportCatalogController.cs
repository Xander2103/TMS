using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Reporting.Controllers;

public record ReportCatalogEntryDto(
    string Id,
    string Category,
    string Title,
    string Description,
    string Kind,
    string? Endpoint,
    string? Route,
    IReadOnlyList<string> Filters,
    string? FileType);

/// <summary>
/// The report catalog: metadata only, filtered to what the caller may actually open. The
/// referenced endpoints/pages enforce their own permissions — this endpoint only decides
/// visibility, so a hidden report is never a security boundary by itself.
/// </summary>
[ApiController]
[Route("api/reports/catalog")]
public class ReportCatalogController : ControllerBase
{
    private readonly IPermissionSetService _permissionSetService;
    private readonly ICurrentUserContext _currentUserContext;

    public ReportCatalogController(IPermissionSetService permissionSetService, ICurrentUserContext currentUserContext)
    {
        _permissionSetService = permissionSetService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<IReadOnlyList<ReportCatalogEntryDto>>> List(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return Ok(Array.Empty<ReportCatalogEntryDto>());
        }

        var permissions = await _permissionSetService.GetPermissionCodesAsync(userId, cancellationToken);

        var visible = ReportCatalog.All
            .Where(r => r.Permissions.Count == 0 || r.Permissions.Any(permissions.Contains))
            .Select(r => new ReportCatalogEntryDto(
                r.Id, r.Category, r.Title, r.Description, r.Kind.ToString(),
                r.Endpoint, r.Route, r.Filters ?? [], r.FileType))
            .ToList();

        return Ok(visible);
    }
}
