namespace TransportationService.Api.Modules.Tenancy.Services;

public interface ITenantContext
{
    Guid TenantId { get; }
}
