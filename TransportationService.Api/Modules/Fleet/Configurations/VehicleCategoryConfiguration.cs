using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class VehicleCategoryConfiguration : LookupEntityTypeConfiguration<VehicleCategory>
{
    protected override string TableName => "vehicle_categories";
}
