using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Entities;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// ACC-H6: an invoice can only be issued when every line is classified (a sales category whose
/// ledger account is known). Harnesses that issue invoices seed this minimal, booked category;
/// order-backed lines pick it up through its Transport system role, manual lines by id.
/// </summary>
public static class AccountingTestSeed
{
    public static async Task<Guid> TransportCategoryAsync(
        TransportationDbContext db, Guid tenantId,
        string accountNumber = "700000", string code = "TRANSPORT", string name = "Transport",
        SalesCategorySystemRole role = SalesCategorySystemRole.Transport)
    {
        var now = DateTime.UtcNow;
        var account = new LedgerAccount
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AccountNumber = accountNumber, Name = "Omzet " + name.ToLowerInvariant(),
            IsActive = true, CreatedAt = now, UpdatedAt = now,
        };
        var category = new SalesCategory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = name, SystemRole = role,
            LedgerAccountId = account.Id, IsActive = true, CreatedAt = now, UpdatedAt = now,
        };
        db.LedgerAccounts.Add(account);
        db.SalesCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>
    /// Simulates a document issued BEFORE ACC-H6 enforced classification at issue: strips the
    /// frozen ledger account (and optionally the category) from the issued lines — the shape legacy
    /// rows have — so the export-time guard and the remediation endpoint can still be exercised.
    /// </summary>
    public static async Task StripLedgerSnapshotAsync(TransportationDbContext db, Guid invoiceId, bool alsoCategory = false)
    {
        var lines = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).ToListAsync();
        foreach (var line in lines)
        {
            line.LedgerAccountId = null;
            line.LedgerAccountNumberSnapshot = null;
            line.LedgerAccountNameSnapshot = null;
            if (alsoCategory)
            {
                line.SalesCategoryId = null;
                line.SalesCategoryNameSnapshot = null;
            }
        }

        await db.SaveChangesAsync();
    }
}
