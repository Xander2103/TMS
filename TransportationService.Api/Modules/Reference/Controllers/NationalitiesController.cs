using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Controllers;

[Route("api/nationalities")]
public class NationalitiesController : LookupControllerBase<Nationality>
{
    public NationalitiesController(ILookupService<Nationality> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.ReferenceDataView;
    protected override string ManagePermission => PermissionCodes.ReferenceDataManage;
}
