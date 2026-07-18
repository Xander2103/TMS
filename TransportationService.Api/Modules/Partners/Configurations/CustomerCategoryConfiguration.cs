using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Configurations;

public class CustomerCategoryConfiguration : LookupEntityTypeConfiguration<CustomerCategory>
{
    protected override string TableName => "customer_categories";
}
