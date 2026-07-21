using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Controllers;

[Route("api/unit-types")]
public class UnitTypesController : LookupControllerBase<UnitType>
{
    public UnitTypesController(ILookupService<UnitType> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.UnitTypesView;
    protected override string ManagePermission => PermissionCodes.UnitTypesManage;
}
