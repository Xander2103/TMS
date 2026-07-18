using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[Route("api/vehicle-categories")]
public class VehicleCategoriesController : LookupControllerBase<VehicleCategory>
{
    public VehicleCategoriesController(ILookupService<VehicleCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.VehicleCategoriesView;
    protected override string ManagePermission => PermissionCodes.VehicleCategoriesManage;
}
