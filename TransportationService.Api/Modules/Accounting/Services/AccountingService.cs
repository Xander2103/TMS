using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Accounting.Services;

public interface IAccountingService
{
    Task<IReadOnlyList<LedgerAccountDto>> ListLedgerAccountsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<LedgerAccountDto> CreateLedgerAccountAsync(SaveLedgerAccountRequest request, CancellationToken cancellationToken);
    Task<LedgerAccountDto?> UpdateLedgerAccountAsync(Guid id, SaveLedgerAccountRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes an UNUSED account; one mapped by a category or snapshotted on an invoice line throws (deactivate instead).</summary>
    Task<bool> DeleteLedgerAccountAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesCategoryDto>> ListSalesCategoriesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<SalesCategoryDto> CreateSalesCategoryAsync(SaveSalesCategoryRequest request, CancellationToken cancellationToken);
    Task<SalesCategoryDto?> UpdateSalesCategoryAsync(Guid id, SaveSalesCategoryRequest request, CancellationToken cancellationToken);

    Task<AccountingHealthDto> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent per-tenant seeding of the default Dutch sales categories (add-if-missing by code; deleted ones stay deleted).</summary>
    Task EnsureSeededAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Tenant-scoped ledger accounts and sales-category → account mappings (corrections wave §7).
/// The mapping here is live configuration only; invoice lines snapshot the resolved account at
/// finalization so historical invoices never move when a mapping changes.
/// </summary>
public class AccountingService : IAccountingService
{
    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public AccountingService(TransportationDbContext db, ITenantContext tenant, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    private Guid TenantId => _tenant.TenantId;

    /// <summary>
    /// Default categories; codes are stable seeds, names/roles editable per tenant. Transport and
    /// supplements count in the diesel-surcharge base by default (the pre-wave behaviour, where
    /// the surcharge ran over the whole order amount); the operator unticks what should not.
    /// </summary>
    private static readonly (string Code, string Name, SalesCategorySystemRole Role, int SortOrder, bool DieselBase)[] DefaultCategories =
    [
        ("TRANSPORT", "Transport", SalesCategorySystemRole.Transport, 0, true),
        ("SUPPLEMENTEN", "Supplementen", SalesCategorySystemRole.Surcharge, 1, true),
        ("DIESEL", "Diesel", SalesCategorySystemRole.Diesel, 2, false),
        ("EUROPALLETS", "Verkoop europallets", SalesCategorySystemRole.None, 3, false),
        ("DIVERS-BINNEN", "Diverse verkoop binnenland", SalesCategorySystemRole.None, 4, false),
        ("DIVERS-BUITEN", "Diverse verkoop buitenland", SalesCategorySystemRole.None, 5, false),
    ];

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a deliberately deleted default category is never resurrected.
        var existing = await _db.SalesCategories.IgnoreQueryFilters()
            .Where(c => c.TenantId == TenantId).Select(c => c.Code).ToListAsync(cancellationToken);
        foreach (var (code, name, role, sortOrder, dieselBase) in DefaultCategories)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            _db.SalesCategories.Add(new SalesCategory
            {
                Id = Guid.NewGuid(), TenantId = TenantId, Code = code, Name = name,
                SystemRole = role, SortOrder = sortOrder, IncludeInDieselBase = dieselBase,
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    // --- Ledger accounts ---

    public async Task<IReadOnlyList<LedgerAccountDto>> ListLedgerAccountsAsync(bool includeInactive, CancellationToken cancellationToken)
        => await _db.LedgerAccounts.AsNoTracking()
            .Where(a => a.TenantId == TenantId && (includeInactive || a.IsActive))
            .OrderBy(a => a.AccountNumber)
            .Select(a => new LedgerAccountDto(a.Id, a.AccountNumber, a.Name, a.ExternalCode, a.Description, a.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<LedgerAccountDto> CreateLedgerAccountAsync(SaveLedgerAccountRequest request, CancellationToken cancellationToken)
    {
        await ValidateAccountAsync(request, existingId: null, cancellationToken);
        var account = new LedgerAccount { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyAccount(account, request);
        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("LedgerAccount", account.Id.ToString(), "Created", null,
            new { account.AccountNumber, account.Name, account.IsActive }, cancellationToken);
        return Map(account);
    }

    public async Task<LedgerAccountDto?> UpdateLedgerAccountAsync(Guid id, SaveLedgerAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await _db.LedgerAccounts.FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }

        await ValidateAccountAsync(request, existingId: id, cancellationToken);
        var oldValues = new { account.AccountNumber, account.Name, account.ExternalCode, account.IsActive };
        ApplyAccount(account, request);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("LedgerAccount", account.Id.ToString(),
            oldValues.IsActive && !account.IsActive ? "Deactivated" : "Updated",
            oldValues, new { account.AccountNumber, account.Name, account.ExternalCode, account.IsActive }, cancellationToken);
        return Map(account);
    }

    public async Task<bool> DeleteLedgerAccountAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await _db.LedgerAccounts.FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (account is null)
        {
            return false;
        }

        var used = await _db.SalesCategories.IgnoreQueryFilters()
                       .AnyAsync(c => c.TenantId == TenantId && c.LedgerAccountId == id, cancellationToken)
                   || await _db.InvoiceLines.IgnoreQueryFilters()
                       .AnyAsync(l => l.TenantId == TenantId && l.LedgerAccountId == id, cancellationToken)
                   || await _db.Set<SalesCategoryLedgerMapping>().IgnoreQueryFilters()
                       .AnyAsync(m => m.TenantId == TenantId && m.LedgerAccountId == id, cancellationToken);
        if (used)
        {
            await _audit.RecordAsync("LedgerAccount", id.ToString(), "DeleteBlocked",
                new { account.AccountNumber, account.Name }, null, cancellationToken);
            throw new DomainValidationException(
                $"Grootboekrekening '{account.AccountNumber} — {account.Name}' is in gebruik en kan niet worden verwijderd. Je kunt de rekening wel deactiveren.");
        }

        _db.Remove(account); // soft delete via interceptor
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("LedgerAccount", id.ToString(), "Deleted",
            new { account.AccountNumber, account.Name }, null, cancellationToken);
        return true;
    }

    // --- Sales categories & mapping ---

    public async Task<IReadOnlyList<SalesCategoryDto>> ListSalesCategoriesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        return await _db.SalesCategories.AsNoTracking()
            .Include(c => c.LedgerMappings)
            .Where(c => c.TenantId == TenantId && (includeInactive || c.IsActive))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new SalesCategoryDto(
                c.Id, c.Code, c.Name, c.SystemRole,
                c.LedgerAccountId, c.LedgerAccount!.AccountNumber, c.LedgerAccount.Name,
                c.IsActive, c.SortOrder,
                c.InvoiceDescriptionNl, c.DefaultUnitCode, c.VatCategoryOverride,
                c.InvoiceDescriptionFr, c.InvoiceDescriptionEn, c.InvoiceDescriptionDe,
                c.IncludeInDieselBase, c.VatTreatmentOverride,
                c.CostCentre, c.DefaultUnitPrice, c.DefaultPricingBasis, c.Notes,
                c.LedgerMappings.Select(m => new SalesCategoryLedgerMappingDto(
                    m.LegalEntityId, m.LedgerAccountId, m.CostCentre)).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesCategoryDto> CreateSalesCategoryAsync(SaveSalesCategoryRequest request, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);
        await ValidateCategoryAsync(request, existingId: null, cancellationToken);
        var category = new SalesCategory { Id = Guid.NewGuid(), TenantId = TenantId };
        await ApplyCategoryAsync(category, request, cancellationToken);
        await ApplyLedgerMappingsAsync(category, request.LedgerMappings, cancellationToken);
        _db.SalesCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("SalesCategory", category.Id.ToString(), "Created", null,
            new { category.Code, category.Name, category.SystemRole, category.LedgerAccountId }, cancellationToken);
        return await MapCategoryAsync(category, cancellationToken);
    }

    public async Task<SalesCategoryDto?> UpdateSalesCategoryAsync(Guid id, SaveSalesCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await _db.SalesCategories.Include(c => c.LedgerMappings)
            .FirstOrDefaultAsync(c => c.TenantId == TenantId && c.Id == id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        await ValidateCategoryAsync(request, existingId: id, cancellationToken);

        // Mapping changes are audited explicitly with old account → new account.
        var oldAccount = category.LedgerAccountId is { } oldId
            ? await _db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Id == oldId).Select(a => a.AccountNumber + " — " + a.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var oldValues = new { category.Code, category.Name, category.SystemRole, category.IsActive, Grootboekrekening = oldAccount };

        await ApplyCategoryAsync(category, request, cancellationToken);
        await ApplyLedgerMappingsAsync(category, request.LedgerMappings, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var newAccount = category.LedgerAccountId is { } newId
            ? await _db.LedgerAccounts.AsNoTracking()
                .Where(a => a.Id == newId).Select(a => a.AccountNumber + " — " + a.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        await _audit.RecordAsync("SalesCategory", category.Id.ToString(), "Updated", oldValues,
            new { category.Code, category.Name, category.SystemRole, category.IsActive, Grootboekrekening = newAccount }, cancellationToken);
        return await MapCategoryAsync(category, cancellationToken);
    }

    public async Task<AccountingHealthDto> GetHealthAsync(CancellationToken cancellationToken)
    {
        var categories = await ListSalesCategoriesAsync(includeInactive: false, cancellationToken);
        return new AccountingHealthDto(categories.Where(c => c.LedgerAccountId is null).ToList());
    }

    // --- Helpers ---

    private async Task ValidateAccountAsync(SaveLedgerAccountRequest request, Guid? existingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            throw new DomainValidationException("accountNumber", "Het rekeningnummer is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        var number = request.AccountNumber.Trim();
        var duplicate = await _db.LedgerAccounts.AnyAsync(
            a => a.TenantId == TenantId && a.AccountNumber == number && a.Id != (existingId ?? Guid.Empty), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("accountNumber", $"Er bestaat al een grootboekrekening met nummer '{number}'.");
        }
    }

    private static void ApplyAccount(LedgerAccount account, SaveLedgerAccountRequest request)
    {
        account.AccountNumber = request.AccountNumber.Trim();
        account.Name = request.Name.Trim();
        account.ExternalCode = string.IsNullOrWhiteSpace(request.ExternalCode) ? null : request.ExternalCode.Trim();
        account.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        account.IsActive = request.IsActive;
    }

    private async Task ValidateCategoryAsync(SaveSalesCategoryRequest request, Guid? existingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new DomainValidationException("code", "De code is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var duplicate = await _db.SalesCategories.AnyAsync(
            c => c.TenantId == TenantId && c.Code == code && c.Id != (existingId ?? Guid.Empty), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een verkoopcategorie met code '{code}'.");
        }

        if (request.SystemRole != SalesCategorySystemRole.None && request.IsActive)
        {
            var roleTaken = await _db.SalesCategories.AnyAsync(
                c => c.TenantId == TenantId && c.SystemRole == request.SystemRole && c.IsActive
                     && c.Id != (existingId ?? Guid.Empty), cancellationToken);
            if (roleTaken)
            {
                throw new DomainValidationException("systemRole",
                    "Er is al een actieve verkoopcategorie met deze systeemrol; per rol kan er maar één actief zijn.");
            }
        }
    }

    private async Task ApplyCategoryAsync(SalesCategory category, SaveSalesCategoryRequest request, CancellationToken cancellationToken)
    {
        if (request.LedgerAccountId is { } accountId && accountId != category.LedgerAccountId)
        {
            // A NEW assignment requires an ACTIVE own-tenant account; an unchanged mapping to a
            // meanwhile-deactivated account stays valid (history/config keeps rendering).
            var account = await _db.LedgerAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == accountId, cancellationToken)
                ?? throw new InvalidTenantReferenceException("grootboekrekening");
            if (!account.IsActive)
            {
                throw new DomainValidationException("ledgerAccountId",
                    "Een inactieve grootboekrekening kan niet meer worden toegewezen. Heractiveer de rekening eerst.");
            }
        }

        // UNCL5305 subset the VAT chain understands (VatTreatmentCatalog); anything else would
        // produce UBL that validators reject.
        var vatOverride = string.IsNullOrWhiteSpace(request.VatCategoryOverride)
            ? null
            : request.VatCategoryOverride.Trim().ToUpperInvariant();
        if (vatOverride is not null && vatOverride is not ("S" or "Z" or "E" or "AE" or "K" or "G"))
        {
            throw new DomainValidationException("vatCategoryOverride",
                "Ongeldige btw-categorie. Toegestaan: S, Z, E, AE, K of G (UNCL5305).");
        }

        // Audit fix: the statutory classification (VatTreatmentOverride) decides the UBL category;
        // a legacy category override may only refine the customer's own domestic treatment. Both
        // set and contradicting each other would give an invoice that declares one thing and
        // charges another.
        if (request.VatTreatmentOverride is { } statutory && vatOverride is not null)
        {
            var info = Partners.Services.VatTreatmentCatalog.Resolve(statutory);
            var expected = Partners.Services.VatTreatmentCatalog
                .ResolveVatCategory(statutory, info.DefaultRatePercent ?? 21m).Code;
            if (statutory != Partners.Entities.VatTreatment.DomesticVat && vatOverride != expected)
            {
                throw new DomainValidationException("vatCategoryOverride",
                    $"De btw-categorie '{vatOverride}' spreekt de fiscale classificatie '{info.Label}' tegen (verwacht '{expected}'). " +
                    "Laat de btw-categorie leeg; ze volgt uit de classificatie.");
            }
        }

        if ((request.CostCentre?.Trim().Length ?? 0) > CostCentreMaxLength)
        {
            throw new DomainValidationException("costCentre",
                $"Een kostenplaats mag maximaal {CostCentreMaxLength} tekens bevatten.");
        }

        category.Code = request.Code.Trim().ToUpperInvariant();
        category.Name = request.Name.Trim();
        category.SystemRole = request.SystemRole;
        category.LedgerAccountId = request.LedgerAccountId;
        category.IsActive = request.IsActive;
        category.SortOrder = request.SortOrder;
        category.InvoiceDescriptionNl = string.IsNullOrWhiteSpace(request.InvoiceDescriptionNl)
            ? null : request.InvoiceDescriptionNl.Trim();
        category.DefaultUnitCode = string.IsNullOrWhiteSpace(request.DefaultUnitCode)
            ? null : request.DefaultUnitCode.Trim().ToUpperInvariant();
        category.VatCategoryOverride = vatOverride;

        // Sprint 5: the commercial article fields. Empty strings are stored as null so a
        // missing description falls through the language chain instead of printing blank.
        category.InvoiceDescriptionFr = Blank(request.InvoiceDescriptionFr);
        category.InvoiceDescriptionEn = Blank(request.InvoiceDescriptionEn);
        category.InvoiceDescriptionDe = Blank(request.InvoiceDescriptionDe);
        category.IncludeInDieselBase = request.IncludeInDieselBase;
        category.VatTreatmentOverride = request.VatTreatmentOverride;
        category.CostCentre = Blank(request.CostCentre);
        category.DefaultUnitPrice = request.DefaultUnitPrice;
        category.DefaultPricingBasis = Blank(request.DefaultPricingBasis);
        category.Notes = Blank(request.Notes);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Replaces the per-entity ledger overrides wholesale: nothing outside the sales code
    /// references an individual mapping row, and a diff would only add ceremony.
    /// </summary>
    /// <summary>
    /// Audit fix: every mapping must point at an own-tenant, active invoicing entity and an
    /// own-tenant ledger account (a new assignment requires an ACTIVE account, like the
    /// primary mapping). Rows are diffed rather than replaced, so an unchanged mapping does not
    /// leave a soft-deleted twin behind on every save.
    /// </summary>
    private async Task ApplyLedgerMappingsAsync(
        SalesCategory category, IReadOnlyList<SalesCategoryLedgerMappingDto>? mappings, CancellationToken cancellationToken)
    {
        var wanted = (mappings ?? []).DistinctBy(m => m.LegalEntityId).ToList();

        if (wanted.Count > 0)
        {
            var entityIds = wanted.Select(m => m.LegalEntityId).ToList();
            var knownEntities = await _db.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == TenantId && entityIds.Contains(e.Id) && e.IsActive)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            if (knownEntities.Count != entityIds.Count)
            {
                throw new InvalidTenantReferenceException("facturerende entiteit");
            }

            var accountIds = wanted.Select(m => m.LedgerAccountId).Distinct().ToList();
            var knownAccounts = await _db.LedgerAccounts.AsNoTracking()
                .Where(a => a.TenantId == TenantId && accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.IsActive })
                .ToListAsync(cancellationToken);
            if (knownAccounts.Count != accountIds.Count)
            {
                throw new InvalidTenantReferenceException("grootboekrekening");
            }

            var inactive = knownAccounts.Where(a => !a.IsActive).Select(a => a.Id).ToHashSet();
            var newlyAssignedInactive = wanted.Any(m =>
                inactive.Contains(m.LedgerAccountId)
                && !category.LedgerMappings.Any(existing =>
                    existing.LegalEntityId == m.LegalEntityId && existing.LedgerAccountId == m.LedgerAccountId));
            if (newlyAssignedInactive)
            {
                throw new DomainValidationException("ledgerMappings",
                    "Een inactieve grootboekrekening kan niet meer worden toegewezen. Heractiveer de rekening eerst.");
            }

            var tooLong = wanted.FirstOrDefault(m => (m.CostCentre?.Trim().Length ?? 0) > CostCentreMaxLength);
            if (tooLong is not null)
            {
                throw new DomainValidationException("ledgerMappings",
                    $"Een kostenplaats mag maximaal {CostCentreMaxLength} tekens bevatten.");
            }
        }

        var removed = category.LedgerMappings
            .Where(existing => wanted.All(m => m.LegalEntityId != existing.LegalEntityId))
            .ToList();
        _db.RemoveRange(removed);
        foreach (var gone in removed)
        {
            category.LedgerMappings.Remove(gone);
        }

        foreach (var mapping in wanted)
        {
            var existing = category.LedgerMappings.FirstOrDefault(m => m.LegalEntityId == mapping.LegalEntityId);
            if (existing is not null)
            {
                existing.LedgerAccountId = mapping.LedgerAccountId;
                existing.CostCentre = Blank(mapping.CostCentre);
                continue;
            }

            var created = new SalesCategoryLedgerMapping
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                SalesCategoryId = category.Id,
                LegalEntityId = mapping.LegalEntityId,
                LedgerAccountId = mapping.LedgerAccountId,
                CostCentre = Blank(mapping.CostCentre),
            };
            // Client-generated id: mark Added explicitly — discovery through the navigation would
            // attach it as Modified and the save would fail with "0 rows affected" (audit fix:
            // this is why per-entity mappings could never be stored before).
            _db.Add(created);
            if (!category.LedgerMappings.Contains(created))
            {
                category.LedgerMappings.Add(created);
            }
        }
    }

    /// <summary>Matches invoice_lines.CostCentreSnapshot (varchar 40): a longer value would break finalization.</summary>
    internal const int CostCentreMaxLength = 40;

    private static LedgerAccountDto Map(LedgerAccount account) =>
        new(account.Id, account.AccountNumber, account.Name, account.ExternalCode, account.Description, account.IsActive);

    private async Task<SalesCategoryDto> MapCategoryAsync(SalesCategory category, CancellationToken cancellationToken)
    {
        var account = category.LedgerAccountId is { } accountId
            ? await _db.LedgerAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == accountId, cancellationToken)
            : null;
        return new SalesCategoryDto(
            category.Id, category.Code, category.Name, category.SystemRole,
            category.LedgerAccountId, account?.AccountNumber, account?.Name,
            category.IsActive, category.SortOrder,
            category.InvoiceDescriptionNl, category.DefaultUnitCode, category.VatCategoryOverride,
            category.InvoiceDescriptionFr, category.InvoiceDescriptionEn, category.InvoiceDescriptionDe,
            category.IncludeInDieselBase, category.VatTreatmentOverride,
            category.CostCentre, category.DefaultUnitPrice, category.DefaultPricingBasis, category.Notes,
            category.LedgerMappings
                .Select(m => new SalesCategoryLedgerMappingDto(m.LegalEntityId, m.LedgerAccountId, m.CostCentre))
                .ToList());
    }
}
