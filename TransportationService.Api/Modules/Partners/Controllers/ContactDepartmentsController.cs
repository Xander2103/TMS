using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Controllers;

[Route("api/contact-departments")]
public class ContactDepartmentsController : LookupControllerBase<ContactDepartment>
{
    public ContactDepartmentsController(ILookupService<ContactDepartment> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.ContactDepartmentsView;
    protected override string ManagePermission => PermissionCodes.ContactDepartmentsManage;
}
