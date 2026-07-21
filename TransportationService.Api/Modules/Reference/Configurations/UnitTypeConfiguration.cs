using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class UnitTypeConfiguration : LookupEntityTypeConfiguration<UnitType>
{
    protected override string TableName => "unit_types";
}
