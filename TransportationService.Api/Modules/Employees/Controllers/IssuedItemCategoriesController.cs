using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Employees.Controllers;

[Route("api/issued-item-categories")]
public class IssuedItemCategoriesController : LookupControllerBase<IssuedItemCategory>
{
    public IssuedItemCategoriesController(ILookupService<IssuedItemCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.IssuedItemsView;
    protected override string ManagePermission => PermissionCodes.InventoryManage;
}
