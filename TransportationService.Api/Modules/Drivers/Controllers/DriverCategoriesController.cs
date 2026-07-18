using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Drivers.Controllers;

[Route("api/driver-categories")]
public class DriverCategoriesController : LookupControllerBase<DriverCategory>
{
    public DriverCategoriesController(ILookupService<DriverCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.DriverCategoriesView;
    protected override string ManagePermission => PermissionCodes.DriverCategoriesManage;
}
