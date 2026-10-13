using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Peppol.Entities;

/// <summary>
/// Per-legal-entity Peppol configuration. Identity (Peppol id/scheme), VAT number, IBAN and
/// address are deliberately NOT duplicated here — they are always read live from the linked
/// <c>LegalEntity</c> so a single source of truth exists; anything missing there surfaces as
/// a gap in the configuration checklist instead of being copied and going stale. No provider
/// secrets are stored: the sandbox provider has none, and real credentials will live in
/// configuration (<c>Peppol:Providers:{key}</c>), never in this entity.
/// </summary>
public class PeppolSettings : AuditableTenantEntity
{
    public Guid LegalEntityId { get; set; }
    public bool Enabled { get; set; }
    public PeppolEnvironment Environment { get; set; } = PeppolEnvironment.Sandbox;

    /// <summary>Key of the <c>IPeppolProvider</c> to use; only "sandbox" is registered today.</summary>
    public string ProviderKey { get; set; } = "sandbox";

    /// <summary>Fallback mailbox used when a customer's delivery preference is e-mail instead of Peppol.</summary>
    public string? InvoiceEmailFallback { get; set; }

    /// <summary>Free-text note appended to outgoing Peppol invoices for this legal entity.</summary>
    public string? DefaultInvoiceNote { get; set; }
}
