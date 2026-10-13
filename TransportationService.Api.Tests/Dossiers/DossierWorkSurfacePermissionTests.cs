using System.Reflection;
using TransportationService.Api.Modules.Dossiers.Controllers;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Controllers;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Hardening 2026-09-10 — the permission matrix behind the dossier work surface (route + price
/// editors) is enforced SERVER-side and is intentionally asymmetric:
/// <list type="bullet">
///   <item>the agreed price (PricingSource OneOff + fixed amount, part of the order's commercial
///   intake) travels with the full order PUT → <c>orders.edit | orders.manage</c>;</item>
///   <item>a whole-order override (PriceIsManual) rides the same PUT but is additionally checked
///   fail-closed in <c>TransportOrderService.UpdateAsync</c> against <c>orders.override_price</c>
///   (see OrderPricingTests);</item>
///   <item>free sales lines / line corrections replace engine output → <c>orders.override_price | orders.manage</c>;</item>
///   <item>creating the linked order for a transport activity is a DOSSIER action (the activity's
///   execution record, same as AddAsync with CreateLinkedOrder since the foundation wave) →
///   <c>dossiers.manage</c>, no <c>orders.create</c>. The frontend route editor is stricter
///   (dossiers.manage AND orders.edit|manage) because it also PUTs the stops right after.</item>
/// </list>
/// A change to any of these attributes must be a deliberate decision that updates this test.
/// </summary>
public class DossierWorkSurfacePermissionTests
{
    private static IReadOnlyList<string> Codes(Type controller, string action) =>
        controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;

    [Fact]
    public void AgreedPrice_TravelsWithTheOrderPut_UnderOrdersEdit()
    {
        Assert.Equal(
            new[] { PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage },
            Codes(typeof(TransportOrdersController), nameof(TransportOrdersController.Update)));
    }

    /// <summary>
    /// Hardening 2026-09-11: the agreed price moved from the whole-order PUT to a price-only
    /// command. Same gate (commercial intake data → orders.edit), so the matrix is unchanged;
    /// the override check stays service-side (OneOffPricingTests).
    /// </summary>
    [Fact]
    public void AgreedPrice_PriceOnlyCommand_KeepsTheOrdersEditGate()
    {
        Assert.Equal(
            new[] { PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage },
            Codes(typeof(TransportOrdersController), nameof(TransportOrdersController.SetOneOffPrice)));
    }

    [Fact]
    public void SalesLines_RequireOverridePrice()
    {
        Assert.Equal(
            new[] { PermissionCodes.OrdersOverridePrice, PermissionCodes.OrdersManage },
            Codes(typeof(TransportOrdersController), nameof(TransportOrdersController.SaveOrderPriceLines)));
    }

    [Fact]
    public void CreatingTheActivityOrder_IsADossierAction_WithoutOrdersCreate()
    {
        var codes = Codes(typeof(DossiersController), nameof(DossiersController.CreateActivityOrder));
        Assert.Equal(new[] { PermissionCodes.DossiersManage }, codes);
        Assert.DoesNotContain(PermissionCodes.OrdersCreate, codes);
    }

    /// <summary>
    /// Step 13 (2026-09-11): the agreed price of a standalone billable activity is a commercial
    /// act on a non-order → its own right <c>dossiers.price</c>; neither <c>dossiers.manage</c>
    /// (dossier structure) nor <c>orders.edit</c> (order intake) implies it.
    /// </summary>
    [Fact]
    public void ActivityPrice_IsItsOwnCommercialRight()
    {
        var codes = Codes(typeof(DossiersController), nameof(DossiersController.SetActivityPrice));
        Assert.Equal(new[] { PermissionCodes.DossiersPrice }, codes);
        Assert.DoesNotContain(PermissionCodes.DossiersManage, codes);
        Assert.DoesNotContain(PermissionCodes.OrdersEdit, codes);
    }

    /// <summary>
    /// Master sprint 2026-09-21 D5: sales LINES of a standalone activity are the same commercial
    /// act as its fixed price → <c>dossiers.price</c> only; without it the request is refused
    /// before the service is reached.
    /// </summary>
    [Fact]
    public void ActivityPriceLines_RequireTheSameCommercialRight()
    {
        var codes = Codes(typeof(DossiersController), nameof(DossiersController.SetActivityPriceLines));
        Assert.Equal(new[] { PermissionCodes.DossiersPrice }, codes);
        Assert.DoesNotContain(PermissionCodes.DossiersManage, codes);
    }

    /// <summary>D7: notes are read with the dossier and written as a dossier edit.</summary>
    [Fact]
    public void DossierNotes_ReadWithView_WriteWithManage()
    {
        Assert.Equal(
            new[] { PermissionCodes.DossiersView, PermissionCodes.DossiersManage },
            Codes(typeof(DossierNotesController), nameof(DossierNotesController.List)));
        foreach (var action in new[] { nameof(DossierNotesController.Create), nameof(DossierNotesController.Update), nameof(DossierNotesController.Delete) })
        {
            Assert.Equal(new[] { PermissionCodes.DossiersManage }, Codes(typeof(DossierNotesController), action));
        }
    }

    /// <summary>
    /// D6: documents on dossier level are read with the dossier and written as a dossier edit; the
    /// order's own list keeps its order rights. The flat by-id routes carry only the coarse outer
    /// gate (any of both families) — the scope decides inside OrderDocumentAccessResolver
    /// (matrix: DossierDocumentScopeTests.FlatRoutePermissionMatrix).
    /// </summary>
    [Fact]
    public void DossierDocuments_ReadWithView_WriteWithManage_FlatRoutesGateOnBothFamilies()
    {
        var controller = typeof(TransportOrderDocumentsController);
        Assert.Equal(new[] { PermissionCodes.DossiersView, PermissionCodes.DossiersManage }, Codes(controller, nameof(TransportOrderDocumentsController.ListForDossier)));
        Assert.Equal(new[] { PermissionCodes.DossiersManage }, Codes(controller, nameof(TransportOrderDocumentsController.CreateForDossier)));
        Assert.Equal(new[] { PermissionCodes.OrdersView, PermissionCodes.OrdersManage }, Codes(controller, nameof(TransportOrderDocumentsController.List)));

        foreach (var action in new[]
                 {
                     nameof(TransportOrderDocumentsController.Update), nameof(TransportOrderDocumentsController.Delete),
                     nameof(TransportOrderDocumentsController.RemoveFile), nameof(TransportOrderDocumentsController.Move),
                 })
        {
            Assert.Equal(
                new[] { PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage, PermissionCodes.DossiersManage },
                Codes(controller, action));
        }

        Assert.Equal(
            new[] { PermissionCodes.OrdersView, PermissionCodes.OrdersManage, PermissionCodes.DossiersView, PermissionCodes.DossiersManage },
            Codes(controller, nameof(TransportOrderDocumentsController.DownloadFile)));
    }
}
