using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class CountryConfiguration : LookupEntityTypeConfiguration<Country>
{
    protected override string TableName => "countries";
}
