using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class ContactDepartmentConfiguration : LookupEntityTypeConfiguration<ContactDepartment>
{
    protected override string TableName => "contact_departments";
}
