namespace TransportationService.Api.Modules.Identity.Services;

/// <summary>
/// Development-only current-user resolution. Reads the trusted X-Dev-User-Id
/// header set by TenantContextMiddleware. Replace with a claims-based
/// implementation once JWT authentication exists.
/// </summary>
public class DevCurrentUserContext : ICurrentUserContext
{
    public Guid? CurrentUserId { get; }

    public DevCurrentUserContext(Guid? currentUserId)
    {
        CurrentUserId = currentUserId;
    }
}
