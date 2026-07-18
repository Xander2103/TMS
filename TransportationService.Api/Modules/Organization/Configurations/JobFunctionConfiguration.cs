using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Organization.Entities;

namespace TransportationService.Api.Modules.Organization.Configurations;

public class JobFunctionConfiguration : LookupEntityTypeConfiguration<JobFunction>
{
    protected override string TableName => "job_functions";
}
