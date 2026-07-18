using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Drivers.Entities;

namespace TransportationService.Api.Modules.Drivers.Configurations;

public class DriverCategoryConfiguration : LookupEntityTypeConfiguration<DriverCategory>
{
    protected override string TableName => "driver_categories";
}
