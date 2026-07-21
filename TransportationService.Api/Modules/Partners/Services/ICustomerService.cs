using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Partners.Dtos;

namespace TransportationService.Api.Modules.Partners.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> SearchAsync(
        string? search, bool? isActive, Guid? categoryId, PageRequest page, CancellationToken cancellationToken);

    Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken, bool canManageFiscal = true);

    Task<CustomerDetailDto?> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken, bool canManageFiscal = true);

    /// <summary>Manual customer-number change (customers.override_number + reason; audited).</summary>
    Task<CustomerDetailDto?> ChangeNumberAsync(Guid id, ChangeCustomerNumberRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SetBlockedAsync(Guid id, SetCustomerBlockedRequest request, CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(Guid id, SetCustomerActiveRequest request, CancellationToken cancellationToken);

    Task<CustomerContactDto?> AddContactAsync(Guid customerId, CreateCustomerContactRequest request, CancellationToken cancellationToken);

    Task<CustomerContactDto?> UpdateContactAsync(Guid customerId, Guid contactId, UpdateCustomerContactRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveContactAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken);
}
