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

    /// <summary>
    /// Which of <paramref name="permissionCodes"/> the user holds — for a read model that gates
    /// several sections on different permissions (the dashboard, PERF-HIGH 6). The database-backed
    /// evaluator answers in one roundtrip; this default asks per code with the same semantics.
    /// </summary>
    async Task<IReadOnlySet<string>> UserPermissionsAmongAsync(
        Guid userId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in permissionCodes)
        {
            if (await UserHasPermissionAsync(userId, code, cancellationToken))
            {
                held.Add(code);
            }
        }

        return held;
    }
}
