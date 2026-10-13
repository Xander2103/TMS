namespace TransportationService.Api.Modules.Authentication.Entities;

/// <summary>
/// A persisted refresh token. Only the SHA-256 hash of the token value is stored, never the
/// raw token. Tokens are single-use: refreshing rotates to a new token and stamps
/// <see cref="RevokedAt"/> / <see cref="ReplacedByTokenHash"/> on the old one. Logout revokes.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 hash (Base64) of the opaque refresh token value.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// Identifies the rotation chain this token belongs to. Every rotation keeps the family id of
    /// its predecessor, so presenting an already-rotated token lets us revoke the whole lineage
    /// (refresh-token reuse detection) instead of only the presented row.
    /// </summary>
    public Guid FamilyId { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && ExpiresAt > nowUtc;
}
