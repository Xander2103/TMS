using TransportationService.Api.Modules.Peppol.Dtos;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolCustomerVerificationService
{
    /// <summary>
    /// Validates a customer's stored Peppol id/scheme against the provider and persists the
    /// outcome (status/timestamp/reference). Null when the customer does not exist in the
    /// current tenant.
    /// </summary>
    Task<CustomerPeppolVerifyResultDto?> VerifyAsync(Guid customerId, CancellationToken cancellationToken);
}
