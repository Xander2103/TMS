using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Controllers;

[Route("api/customer-categories")]
public class CustomerCategoriesController : LookupControllerBase<CustomerCategory>
{
    public CustomerCategoriesController(ILookupService<CustomerCategory> service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
        : base(service, currentUser, authorization) { }

    protected override string ViewPermission => PermissionCodes.CustomerCategoriesView;
    protected override string ManagePermission => PermissionCodes.CustomerCategoriesManage;
}
