using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Reference.Entities;

namespace TransportationService.Api.Modules.Reference.Configurations;

public class ContractTypeConfiguration : LookupEntityTypeConfiguration<ContractType>
{
    protected override string TableName => "contract_types";
}
