using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// MD-H3 legacy policy. An issued invoice's party snapshot becomes FINAL
/// (<see cref="Invoice.PartySnapshotFrozenAt"/>) and from then on nothing reads live master data.
/// Rows issued before that marker existed are frozen ONCE:
/// <list type="bullet">
/// <item>issued after the accounting remediation (<c>IssuedAt</c> set): the snapshot WAS taken at
/// issue — an empty field really was empty — so no field is filled; only the logo, which the PDF
/// used to read live, is pinned to the version current at deployment;</item>
/// <item>older rows (no <c>IssuedAt</c>): fields that were never frozen are filled once from the
/// master data as it stands now — the only source left — and the row is flagged
/// <see cref="Invoice.PartySnapshotBackfilled"/>.</item>
/// </list>
/// Runs at startup (idempotent) and lazily before a not-yet-frozen issued invoice is read.
/// </summary>
public static class InvoicePartySnapshotBackfill
{
    private const int BatchSize = 200;

    public static async Task<int> SyncAsync(TransportationDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Invoices.IgnoreQueryFilters()
                .Where(i => i.Status != InvoiceStatus.Draft && i.PartySnapshotFrozenAt == null)
                .OrderBy(i => i.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var invoice in batch)
            {
                await FreezeAsync(db, invoice, DateTime.UtcNow, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            total += batch.Count;
        }

        if (total > 0)
        {
            logger?.LogInformation("MD-H3: froze the party snapshot of {Count} legacy issued invoice(s).", total);
        }

        return total;
    }

    /// <summary>Freezes one issued invoice if it is not final yet; true when it changed it (then saved).</summary>
    public static async Task<bool> EnsureFrozenAsync(TransportationDbContext db, Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, cancellationToken);
        if (invoice is null || invoice.Status == InvoiceStatus.Draft || invoice.PartySnapshotIsFinal)
        {
            return false;
        }

        await FreezeAsync(db, invoice, DateTime.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Freezes in place (not saved). Never overwrites a value that was frozen.</summary>
    public static async Task FreezeAsync(TransportationDbContext db, Invoice invoice, DateTime now, CancellationToken cancellationToken)
    {
        var entity = invoice.LegalEntityId is { } entityId
            ? await db.LegalEntities.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == invoice.TenantId && e.Id == entityId, cancellationToken)
            : null;
        invoice.SellerLogoStorageKey ??= entity?.LogoStorageKey;

        if (invoice.IssuedAt is not null)
        {
            invoice.PartySnapshotFrozenAt = invoice.IssuedAt;
            return;
        }

        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == invoice.TenantId && c.Id == invoice.CustomerId, cancellationToken);
        if (entity is not null)
        {
            invoice.SellerName ??= entity.TradingName ?? entity.LegalName;
            invoice.SellerLegalName ??= entity.LegalName;
            invoice.SellerCompanyNumber ??= entity.CompanyNumber;
            invoice.SellerVatNumber ??= entity.VatNumber;
            invoice.SellerIban ??= entity.Iban;
            invoice.SellerBic ??= entity.Bic;
            invoice.SellerStreet ??= entity.Street;
            invoice.SellerHouseNumber ??= entity.HouseNumber;
            invoice.SellerPostalCode ??= entity.PostalCode;
            invoice.SellerCity ??= entity.City;
            invoice.SellerCountryCode ??= entity.CountryCode?.Trim().ToUpperInvariant();
            invoice.SellerPeppolScheme ??= entity.PeppolScheme;
            invoice.SellerPeppolId ??= entity.PeppolId;
            invoice.SellerInvoiceFooter ??= entity.InvoiceFooter;
            invoice.SellerAddressLine ??= InvoiceService.AddressLine(entity.Street, entity.HouseNumber, entity.PostalCode, entity.City, null);
        }

        if (customer is not null)
        {
            invoice.CustomerVatNumberSnapshot ??= customer.VatNumber;
            invoice.CustomerNameSnapshot ??= customer.Name;
            invoice.CustomerLegalNameSnapshot ??= customer.LegalName;
            invoice.CustomerCompanyNumberSnapshot ??= customer.CompanyNumber;
            invoice.CustomerStreetSnapshot ??= customer.Street;
            invoice.CustomerHouseNumberSnapshot ??= customer.HouseNumber;
            invoice.CustomerPostalCodeSnapshot ??= customer.PostalCode;
            invoice.CustomerCitySnapshot ??= customer.City;
            invoice.CustomerCountryCodeSnapshot ??= customer.CountryCode?.Trim().ToUpperInvariant();
            invoice.CustomerPeppolSchemeSnapshot ??= customer.PeppolScheme;
            invoice.CustomerPeppolIdSnapshot ??= customer.PeppolId;
            invoice.CustomerBuyerReferenceSnapshot ??= customer.BuyerReference;
        }

        invoice.PartySnapshotFrozenAt = now;
        invoice.PartySnapshotBackfilled = true;
    }
}
