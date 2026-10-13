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
/// Persoonlijke weergavevoorkeur (licht / donker / systeem) van de ingelogde gebruiker —
/// de tegenhanger van <see cref="MyLanguageController"/> op dezelfde User-rij. Bewust alleen
/// [Authorize], geen permissiecode: strikt self-scoped (eigen rij, eigen tenant) en
/// presentatie-only — een thema beïnvloedt nooit rechten, tenant- of bedrijfsinstellingen.
/// Geregistreerd in de Phase10-allowlist.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/theme")]
public class MyThemeController : ControllerBase
{
    public sealed record SetMyThemeRequest(string Theme);
    public sealed record MyThemeDto(string? Theme);

    private readonly TransportationDbContext _dbContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public MyThemeController(
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
    public async Task<ActionResult<MyThemeDto>> Set(
        [FromBody] SetMyThemeRequest request, CancellationToken cancellationToken)
    {
        if (ThemePreferences.NormalizeOrNull(request.Theme) is not { } normalized)
        {
            return BadRequest(new
            {
                message = "Kies system, light of dark.",
                code = "common.unsupported_theme",
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

        var old = user.ThemePreference;
        if (old != normalized)
        {
            user.ThemePreference = normalized;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("User", user.Id.ToString(), "ThemeChanged",
                new { Theme = old }, new { Theme = normalized }, cancellationToken);
        }

        return Ok(new MyThemeDto(normalized));
    }
}
