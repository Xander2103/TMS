namespace TransportationService.Api.Modules.Identity.Services;

public interface IPermissionAuthorizationService
{
    Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken);

    /// <summary>Any-of check; the database-backed evaluator answers it in one roundtrip.</summary>
    async Task<bool> UserHasAnyPermissionAsync(Guid userId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
    {
        foreach (var code in permissionCodes)
        {
            if (await UserHasPermissionAsync(userId, code, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
