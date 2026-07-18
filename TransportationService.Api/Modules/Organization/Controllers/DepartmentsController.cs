using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Organization.Controllers;

[Route("api/departments")]
public class DepartmentsController : LookupControllerBase<Department>
{
    public DepartmentsController(ILookupService<Department> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.DepartmentsView;
    protected override string ManagePermission => PermissionCodes.DepartmentsManage;
}
