using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class LanguageConfiguration : LookupEntityTypeConfiguration<Language>
{
    protected override string TableName => "languages";
}
