using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[Route("api/trailer-categories")]
public class TrailerCategoriesController : LookupControllerBase<TrailerCategory>
{
    public TrailerCategoriesController(ILookupService<TrailerCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.TrailerCategoriesView;
    protected override string ManagePermission => PermissionCodes.TrailerCategoriesManage;
}
