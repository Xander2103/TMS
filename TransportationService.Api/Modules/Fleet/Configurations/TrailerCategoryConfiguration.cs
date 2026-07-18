using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Configurations;

public class TrailerCategoryConfiguration : LookupEntityTypeConfiguration<TrailerCategory>
{
    protected override string TableName => "trailer_categories";
}
