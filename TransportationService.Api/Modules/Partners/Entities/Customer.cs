using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>
/// A customer (shipper/consignor) of the transport company. Master record used across
/// quoting, order intake and invoicing.
/// </summary>
public class Customer : AuditableTenantEntity
{
    public string CustomerNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }

    public Guid? CategoryId { get; set; }

    // Primary contact details for the organisation
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }

    // Registered / visiting address
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    // Commercial terms
    public string? InvoiceEmail { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public string? DefaultLanguageCode { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Credit/operational block preventing new orders. Independent of active/inactive.</summary>
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    public List<CustomerContact> Contacts { get; set; } = [];
}
