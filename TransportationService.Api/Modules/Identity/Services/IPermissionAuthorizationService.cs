namespace TransportationService.Api.Modules.Identity.Services;

public interface IPermissionAuthorizationService
{
    Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken);
}
