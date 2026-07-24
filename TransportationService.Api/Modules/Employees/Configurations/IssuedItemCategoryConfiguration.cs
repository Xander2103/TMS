using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Configurations;

public class IssuedItemCategoryConfiguration : LookupEntityTypeConfiguration<IssuedItemCategory>
{
    protected override string TableName => "issued_item_categories";
}
