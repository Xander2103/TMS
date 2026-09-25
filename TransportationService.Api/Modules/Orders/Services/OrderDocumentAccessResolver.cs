using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>What a caller wants to do with ONE document on the flat <c>api/order-documents/{id}</c> routes.</summary>
public enum OrderDocumentOperation
{
    /// <summary>Download the file.</summary>
    Download,

    /// <summary>Attach a file (the historical upload gate also accepts <c>orders.create</c>).</summary>
    Upload,

    /// <summary>Change metadata, delete, remove the file, move.</summary>
    Write,
}

public enum OrderDocumentAccessOutcome
{
    Allowed,
    NotFound,
    Forbidden,
}

/// <param name="Scope">The scope the decision was made for — the service call must run in exactly this scope.</param>
/// <param name="RequiredPermissions">The any-of codes that were missing (Forbidden only).</param>
public sealed record OrderDocumentAccess(
    OrderDocumentAccessOutcome Outcome, DossierDocumentScope Scope, IReadOnlyList<string> RequiredPermissions);

public interface IOrderDocumentAccessResolver
{
    /// <summary>Loads the document's scope and decides whether the current user may perform <paramref name="operation"/> on it.</summary>
    Task<OrderDocumentAccess> AuthorizeAsync(Guid documentId, OrderDocumentOperation operation, CancellationToken cancellationToken);

    /// <summary>Same decision for a scope that is known without a document (the TARGET of a move).</summary>
    Task<OrderDocumentAccess> AuthorizeScopeAsync(DossierDocumentScope scope, OrderDocumentOperation operation, CancellationToken cancellationToken);
}

/// <summary>
/// D6: THE permission decision for the flat order-document routes. One entity serves two levels, so
/// the required right depends on the row: a DOSSIER document follows the dossier
/// (<c>dossiers.view</c> / <c>dossiers.manage</c>), an ORDER document keeps the order rights it
/// always had. The controller attributes are only the coarse outer gate (any of both families);
/// this resolver is the single place where scope → permission is decided. Fail-closed: no user or
/// no matching right = Forbidden; an unknown (or foreign-tenant) document = NotFound.
/// </summary>
public class OrderDocumentAccessResolver : IOrderDocumentAccessResolver
{
    private readonly ITransportOrderDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _permissions;

    public OrderDocumentAccessResolver(
        ITransportOrderDocumentService documents, ICurrentUserContext currentUser, IPermissionAuthorizationService permissions)
    {
        _documents = documents;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    /// <summary>The permission matrix (any-of per cell).</summary>
    public static IReadOnlyList<string> RequiredPermissions(DossierDocumentScope scope, OrderDocumentOperation operation) =>
        (scope, operation) switch
        {
            (DossierDocumentScope.Dossier, OrderDocumentOperation.Download) => [PermissionCodes.DossiersView, PermissionCodes.DossiersManage],
            (DossierDocumentScope.Dossier, _) => [PermissionCodes.DossiersManage],
            (_, OrderDocumentOperation.Download) => [PermissionCodes.OrdersView, PermissionCodes.OrdersManage],
            (_, OrderDocumentOperation.Upload) => [PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage],
            _ => [PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage],
        };

    public async Task<OrderDocumentAccess> AuthorizeAsync(
        Guid documentId, OrderDocumentOperation operation, CancellationToken cancellationToken)
    {
        if (await _documents.GetScopeAsync(documentId, cancellationToken) is not { } scope)
        {
            return new OrderDocumentAccess(OrderDocumentAccessOutcome.NotFound, DossierDocumentScope.Order, []);
        }

        return await AuthorizeScopeAsync(scope, operation, cancellationToken);
    }

    public async Task<OrderDocumentAccess> AuthorizeScopeAsync(
        DossierDocumentScope scope, OrderDocumentOperation operation, CancellationToken cancellationToken)
    {
        var required = RequiredPermissions(scope, operation);
        if (_currentUser.CurrentUserId is { } userId)
        {
            foreach (var code in required)
            {
                if (await _permissions.UserHasPermissionAsync(userId, code, cancellationToken))
                {
                    return new OrderDocumentAccess(OrderDocumentAccessOutcome.Allowed, scope, required);
                }
            }
        }

        return new OrderDocumentAccess(OrderDocumentAccessOutcome.Forbidden, scope, required);
    }
}
