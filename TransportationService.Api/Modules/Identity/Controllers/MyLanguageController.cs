using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Controllers;

/// <summary>
/// UI-taalvoorkeur van de ingelogde gebruiker (intern én portaal — zelfde User-kolom
/// als het bestaande klantportaal-endpoint). Bewust alleen [Authorize], geen
/// permissiecode: strikt self-scoped (eigen rij, eigen tenant) en presentatie-only —
/// taal beïnvloedt nooit permissies. Geregistreerd in de Phase10-allowlist.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/language")]
public class MyLanguageController : ControllerBase
{
    public sealed record SetMyLanguageRequest(string Language);
    public sealed record MyLanguageDto(string? Language);

    private readonly TransportationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public MyLanguageController(
        TransportationDbContext dbContext,
        ICurrentUserContext currentUserContext,
        ITenantContext tenantContext,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _currentUserContext = currentUserContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    [HttpPut]
    public async Task<ActionResult<MyLanguageDto>> Set(
        [FromBody] SetMyLanguageRequest request, CancellationToken cancellationToken)
    {
        if (SupportedLanguages.NormalizeOrNull(request.Language) is not { } normalized)
        {
            return BadRequest(new
            {
                message = "Kies nl, fr of en.",
                code = "common.unsupported_language",
            });
        }

        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var old = user.PreferredLanguageCode;
        if (old != normalized)
        {
            user.PreferredLanguageCode = normalized;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("User", user.Id.ToString(), "LanguageChanged",
                new { Language = old }, new { Language = normalized }, cancellationToken);
        }

        return Ok(new MyLanguageDto(normalized));
    }
}
