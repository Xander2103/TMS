using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class NationalityConfiguration : LookupEntityTypeConfiguration<Nationality>
{
    protected override string TableName => "nationalities";
}
