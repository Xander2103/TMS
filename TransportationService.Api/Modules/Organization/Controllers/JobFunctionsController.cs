using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Organization.Controllers;

[Route("api/job-functions")]
public class JobFunctionsController : LookupControllerBase<JobFunction>
{
    public JobFunctionsController(ILookupService<JobFunction> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.JobFunctionsView;
    protected override string ManagePermission => PermissionCodes.JobFunctionsManage;
}
