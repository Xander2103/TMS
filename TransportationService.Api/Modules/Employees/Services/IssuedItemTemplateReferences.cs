using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// MD-H9 (master-data acceptance 2026-10-18; overlaps the Warehouse acceptance): an issued-item
/// template with stock and movements could be deleted — the movements answered 404 and /inventory
/// showed "Nog geen artikelen". Stock on hand, stock movements, items issued to employees and
/// reorder proposals are history: the template is deactivated, never deleted. Variants and
/// attributes without stock or movements are the template's own configuration.
/// Shared on purpose: the Warehouse remediation reuses this guard for its own flows.
/// <para>
/// WH-B1 (warehouse remediation 2026-10-18) extends the same guard to the children: a variant was
/// deletable as soon as its CURRENT stock was 0, even with movements, issued items or reorder
/// proposals behind it; an attribute value (option) used by variants could be removed. Both now
/// answer the same 409 <c>record.referenced</c> with "deactivate instead" as the way out.
/// </para>
/// </summary>
public static class IssuedItemTemplateReferences
{
    public const string DeactivateHint =
        "Zet het artikel inactief: de voorraadhistoriek blijft leesbaar en het kan niet meer uitgereikt worden.";

    public const string VariantDeactivateHint =
        "Zet de variant inactief: de historiek blijft leesbaar en de variant kan niet meer uitgereikt of ontvangen worden.";

    public const string OptionDeactivateHint =
        "Zet de waarde inactief: bestaande varianten behouden ze, nieuwe varianten kunnen ze niet meer kiezen.";

    public static async Task<ReferenceReport> ForTemplateAsync(
        TransportationDbContext db, Guid tenantId, Guid templateId, CancellationToken cancellationToken)
    {
        // Template-level stock (no variants) counts as stock on hand too; it is normally also
        // covered by its movements, but a cached stock never depends on that being true.
        var templateStock = Math.Abs(await db.IssuedItemTemplates
            .Where(t => t.TenantId == tenantId && t.Id == templateId)
            .Select(t => t.CurrentStock)
            .FirstOrDefaultAsync(cancellationToken));
        var variantStock = await db.IssuedItemVariants
            .Where(v => v.TenantId == tenantId && v.TemplateId == templateId && v.CurrentStock != 0)
            .CountAsync(cancellationToken);
        return new ReferenceReport()
            .Add("templateStock", templateStock, "eenheid in voorraad", "eenheden in voorraad")
            .Add("stock", variantStock, "variant met voorraad", "varianten met voorraad")
            .Add("movements", await db.StockMovements.CountAsync(m => m.TenantId == tenantId && m.TemplateId == templateId, cancellationToken),
                "voorraadbeweging", "voorraadbewegingen")
            .Add("issuedItems", await db.EmployeeIssuedItems.CountAsync(i => i.TenantId == tenantId && i.TemplateId == templateId, cancellationToken),
                "uitgereikt item", "uitgereikte items")
            .Add("reorderProposals", await db.ReorderProposals.CountAsync(r => r.TenantId == tenantId && r.TemplateId == templateId, cancellationToken),
                "bestelvoorstel", "bestelvoorstellen");
    }

    /// <summary>A variant is history as soon as stock, a movement, an issuance or a reorder proposal points at it.</summary>
    public static async Task<ReferenceReport> ForVariantAsync(
        TransportationDbContext db, Guid tenantId, Guid variantId, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("stock", Math.Abs(await db.IssuedItemVariants
                    .Where(v => v.TenantId == tenantId && v.Id == variantId)
                    .Select(v => v.CurrentStock)
                    .FirstOrDefaultAsync(cancellationToken)),
                "eenheid in voorraad", "eenheden in voorraad")
            .Add("movements", await db.StockMovements.CountAsync(m => m.TenantId == tenantId && m.VariantId == variantId, cancellationToken),
                "voorraadbeweging", "voorraadbewegingen")
            .Add("issuedItems", await db.EmployeeIssuedItems.CountAsync(i => i.TenantId == tenantId && i.VariantId == variantId, cancellationToken),
                "uitgereikt item", "uitgereikte items")
            .Add("reorderProposals", await db.ReorderProposals.CountAsync(r => r.TenantId == tenantId && r.VariantId == variantId, cancellationToken),
                "bestelvoorstel", "bestelvoorstellen");

    /// <summary>An attribute value used by (live) variants is part of their identity.</summary>
    public static async Task<ReferenceReport> ForAttributeOptionAsync(
        TransportationDbContext db, Guid tenantId, Guid optionId, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("variants", await db.IssuedItemVariantValues
                    .Where(v => v.TenantId == tenantId && v.AttributeOptionId == optionId)
                    .Join(db.IssuedItemVariants, value => value.VariantId, variant => variant.Id, (value, variant) => variant.Id)
                    .Distinct()
                    .CountAsync(cancellationToken),
                "variant", "varianten");
}
