using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Organization.Configurations;

public class DepartmentConfiguration : LookupEntityTypeConfiguration<Department>
{
    protected override string TableName => "departments";
}
