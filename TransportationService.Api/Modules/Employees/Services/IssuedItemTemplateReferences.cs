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
/// </summary>
public static class IssuedItemTemplateReferences
{
    public const string DeactivateHint =
        "Zet het artikel inactief: de voorraadhistoriek blijft leesbaar en het kan niet meer uitgereikt worden.";

    public static async Task<ReferenceReport> ForTemplateAsync(
        TransportationDbContext db, Guid tenantId, Guid templateId, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("stock", await db.IssuedItemVariants
                    .Where(v => v.TenantId == tenantId && v.TemplateId == templateId && v.CurrentStock != 0)
                    .CountAsync(cancellationToken),
                "variant met voorraad", "varianten met voorraad")
            .Add("movements", await db.StockMovements.CountAsync(m => m.TenantId == tenantId && m.TemplateId == templateId, cancellationToken),
                "voorraadbeweging", "voorraadbewegingen")
            .Add("issuedItems", await db.EmployeeIssuedItems.CountAsync(i => i.TenantId == tenantId && i.TemplateId == templateId, cancellationToken),
                "uitgereikt item", "uitgereikte items")
            .Add("reorderProposals", await db.ReorderProposals.CountAsync(r => r.TenantId == tenantId && r.TemplateId == templateId, cancellationToken),
                "bestelvoorstel", "bestelvoorstellen");
}
