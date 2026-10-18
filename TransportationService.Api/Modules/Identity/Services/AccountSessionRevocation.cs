using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Identity.Services;

/// <summary>
/// Ends every way a user can still get in — the ONE implementation behind admin deactivation,
/// blocking and employee offboarding (MD-H6 / Security H2): refresh tokens revoked, pending
/// activation / password-reset links revoked, and the security stamp rotated so every access
/// token already issued is rejected on its next request (AccountStateAuthorizationFilter). Not
/// saved here: the caller's SaveChanges commits it with the rest of its change.
/// </summary>
public static class AccountSessionRevocation
{
    public static async Task RevokeAllAsync(TransportationDbContext db, User user, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var token in await db.Set<RefreshToken>()
                     .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                     .ToListAsync(cancellationToken))
        {
            token.RevokedAt = nowUtc;
        }

        foreach (var link in await db.Set<UserSecurityToken>()
                     .Where(t => t.UserId == user.Id && t.UsedAt == null && t.RevokedAt == null)
                     .ToListAsync(cancellationToken))
        {
            link.RevokedAt = nowUtc;
        }

        user.SecurityStamp = Guid.NewGuid();
    }
}
