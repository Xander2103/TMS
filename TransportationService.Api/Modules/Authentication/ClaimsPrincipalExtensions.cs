using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TransportationService.Api.Modules.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        // JwtSecurityTokenHandler maps "sub" to ClaimTypes.NameIdentifier by default.
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static Guid? GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(AppClaimTypes.TenantId);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
