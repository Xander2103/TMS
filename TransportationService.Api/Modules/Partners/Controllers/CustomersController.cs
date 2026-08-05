using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;
    private readonly ICompanyRegistryProvider _registryProvider;
    private readonly IPeppolCustomerVerificationService _peppolVerification;
    private readonly ICustomerHistoryService _historyService;

    public CustomersController(ICustomerService customerService, ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization, ICompanyRegistryProvider registryProvider,
        IPeppolCustomerVerificationService peppolVerification, ICustomerHistoryService historyService)
    {
        _customerService = customerService;
        _currentUser = currentUser;
        _authorization = authorization;
        _registryProvider = registryProvider;
        _peppolVerification = peppolVerification;
        _historyService = historyService;
    }

    /// <summary>Fiscal/Peppol/bank mutations are gated on customers.manage_fiscal.</summary>
    private async Task<bool> HasFiscalAccessAsync(CancellationToken cancellationToken) =>
        _currentUser.CurrentUserId is { } userId
        && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.CustomersManageFiscal, cancellationToken);

    /// <summary>The coherent VAT-treatment catalog (labels, rates, legal texts) for the UI.</summary>
    [HttpGet("vat-treatments")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public ActionResult<IReadOnlyList<VatTreatmentCatalog.VatTreatmentInfo>> VatTreatments()
    {
        return Ok(VatTreatmentCatalog.All);
    }

    /// <summary>Authoritative Peppol scheme (EAS) catalog for the grouped Peppol control.</summary>
    [HttpGet("peppol-schemes")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public ActionResult<IReadOnlyList<PeppolSchemeDto>> GetPeppolSchemes()
        => Ok(PeppolSchemeCatalog.All
            .Select(s => new PeppolSchemeDto(s.Code, s.Label, s.CountryCode))
            .ToList());

    /// <summary>
    /// Official company-registry lookup. Without a configured provider this reports
    /// configured=false so the UI can show its manual-entry fallback state.
    /// </summary>
    [HttpPost("registry-lookup")]
    [RequirePermission(PermissionCodes.CustomersCreate, PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> RegistryLookup(CompanyRegistryLookupRequest request, CancellationToken cancellationToken)
    {
        if (!_registryProvider.IsConfigured)
        {
            return Ok(new { configured = false, result = (CompanyRegistryResult?)null });
        }

        var result = await _registryProvider.LookupAsync(request.Number?.Trim() ?? string.Empty, cancellationToken);
        return Ok(new { configured = true, result });
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _customerService.SearchAsync(search, isActive, categoryId, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<CustomerDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }

    /// <summary>Readable change history (old/new values, actor, Dutch labels) for the Historiek tab.</summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(PermissionCodes.CustomersView)]
    public async Task<ActionResult<CustomerHistoryPageDto>> GetHistory(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var history = await _historyService.GetHistoryAsync(id, page, pageSize, category, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CustomersCreate)]
    public async Task<ActionResult<CustomerDetailDto>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Naam is verplicht." });
        }

        var created = await _customerService.CreateAsync(request, cancellationToken, await HasFiscalAccessAsync(cancellationToken));
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerDetailDto>> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Naam is verplicht." });
        }

        var updated = await _customerService.UpdateAsync(id, request, cancellationToken, await HasFiscalAccessAsync(cancellationToken));
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.CustomersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _customerService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>Manual customer-number change: accounting-significant, so gated + audited.</summary>
    [HttpPost("{id:guid}/number")]
    [RequirePermission(PermissionCodes.CustomersOverrideNumber)]
    public async Task<ActionResult<CustomerDetailDto>> ChangeNumber(
        Guid id, ChangeCustomerNumberRequest request, CancellationToken cancellationToken)
    {
        var updated = await _customerService.ChangeNumberAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/blocked")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> SetBlocked(Guid id, SetCustomerBlockedRequest request, CancellationToken cancellationToken)
    {
        return await _customerService.SetBlockedAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/active")]
    [RequirePermission(PermissionCodes.CustomersDeactivate)]
    public async Task<IActionResult> SetActive(Guid id, SetCustomerActiveRequest request, CancellationToken cancellationToken)
    {
        return await _customerService.SetActiveAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/contacts")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerContactDto>> AddContact(Guid id, CreateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Voor- en achternaam zijn verplicht." });
        }

        var contact = await _customerService.AddContactAsync(id, request, cancellationToken);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpPut("{id:guid}/contacts/{contactId:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<ActionResult<CustomerContactDto>> UpdateContact(Guid id, Guid contactId, UpdateCustomerContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Voor- en achternaam zijn verplicht." });
        }

        var contact = await _customerService.UpdateContactAsync(id, contactId, request, cancellationToken);
        return contact is null ? NotFound() : Ok(contact);
    }

    /// <summary>
    /// "Peppol-gegevens controleren": looks the customer's Peppol participant up at the
    /// configured provider and persists the outcome (status, timestamp, reference).
    /// </summary>
    [HttpPost("{id:guid}/peppol/verify")]
    [RequirePermission(PermissionCodes.PeppolValidate)]
    public async Task<ActionResult<CustomerPeppolVerifyResultDto>> VerifyPeppol(Guid id, CancellationToken cancellationToken)
    {
        var result = await _peppolVerification.VerifyAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    [RequirePermission(PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken cancellationToken)
    {
        return await _customerService.RemoveContactAsync(id, contactId, cancellationToken) ? NoContent() : NotFound();
    }
}
