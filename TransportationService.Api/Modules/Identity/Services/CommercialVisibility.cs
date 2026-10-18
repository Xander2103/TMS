namespace TransportationService.Api.Modules.Identity.Services;

/// <summary>
/// Security M1: who may read commercial money on operational screens (order/dossier prices,
/// price lines, invoice links, incident costs and charges). There is no separate "view prices"
/// code: a caller sees prices when they hold any permission whose work is commercial — setting or
/// approving prices, tariffs, invoicing or financial reporting. An operational viewer (Dispatcher:
/// orders.view, dossiers.view, incidents.manage) gets the same DTOs with the money left out.
/// The frontend mirrors this list in <c>features/auth/commercialVisibility.ts</c>.
/// </summary>
public static class CommercialVisibility
{
    public static readonly IReadOnlyList<string> PriceViewerPermissions =
    [
        PermissionCodes.OrdersCreate,
        PermissionCodes.OrdersEdit,
        PermissionCodes.OrdersManage,
        PermissionCodes.OrdersOverridePrice,
        PermissionCodes.OrdersLockPrice,
        PermissionCodes.OrdersConfirmIncompletePrice,
        PermissionCodes.DossiersPrice,
        PermissionCodes.TariffsView,
        PermissionCodes.TariffsManage,
        PermissionCodes.InvoicesView,
        PermissionCodes.AccountingView,
        PermissionCodes.ProfitabilityView,
        PermissionCodes.ProblemsApproveCharge,
    ];

    public static async Task<bool> CanViewPricesAsync(
        IPermissionAuthorizationService permissions, Guid? userId, CancellationToken cancellationToken) =>
        userId is { } id && await permissions.UserHasAnyPermissionAsync(id, PriceViewerPermissions, cancellationToken);
}
