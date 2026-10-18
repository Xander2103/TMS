using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// MD-H8/M12 (master-data acceptance 2026-10-18): the generic lookup DELETE soft-deleted a unit
/// type, a vehicle category or a customer category that records still pointed at — a vehicle then
/// showed "Categorie —" and a historical CMR printed a unit code that had been re-created with a
/// different meaning. A lookup type with history registers ONE explicit guard: it names what still
/// references a row (refused with 409 + deactivate hint) and whether codes of deleted rows stay
/// reserved (only where history stores the CODE instead of the id). Lookups without a guard keep
/// the plain delete.
/// </summary>
public interface ILookupReferenceGuard<in TEntity> where TEntity : LookupEntity
{
    /// <summary>"Deze eenheid" — the subject of the refusal sentence.</summary>
    string Subject { get; }

    /// <summary>True when history stores the code itself: a deleted row's code is never reused.</summary>
    bool ReservesDeletedCodes => false;

    Task<ReferenceReport> ReferencesAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// MD-H8 final: normalizes an explicitly typed code. Called by EVERY mutation path — the generic
    /// lookup create/update and any type-specific service — so they cannot disagree. Default: as typed.
    /// </summary>
    string NormalizeCode(string code) => code;

    /// <summary>
    /// MD-H8 final: called by EVERY mutation path before a row's code changes. Where the code is the
    /// identity history depends on, a referenced row refuses the change. Default: free.
    /// </summary>
    Task EnsureCodeMayChangeAsync(TEntity entity, string newCode, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class LookupGuardText
{
    public const string DeactivateHint = "Zet het record inactief: de historiek blijft leesbaar en het kan niet meer gekozen worden voor nieuw werk.";
}

/// <summary>
/// Units are stored by CODE on goods lines, order lines, price lines and sales categories, and by id
/// on tariff rules, service options, customer unit mappings and scheduled price adjustments. A
/// referenced unit is never deleted, its code never changes (history would silently follow the new
/// code or orphan), and the code of a deleted unit is never handed out again. This guard is the ONE
/// invariant for every UnitType mutation path: the generic <c>/api/unit-types/{id}</c> lookup PUT and
/// the unit master service (<c>/master</c>) both call it.
/// </summary>
public sealed partial class UnitTypeReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<UnitType>
{
    public string Subject => "Deze eenheid";

    public bool ReservesDeletedCodes => true;

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z0-9_-]{2,20}$")]
    private static partial System.Text.RegularExpressions.Regex CodePattern();

    public string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(normalized))
        {
            throw new DomainValidationException("code", "Code moet 2-20 tekens zijn (A-Z, 0-9, - of _).");
        }

        return normalized;
    }

    public async Task EnsureCodeMayChangeAsync(UnitType unit, string newCode, CancellationToken cancellationToken)
    {
        if (string.Equals(unit.Code, newCode, StringComparison.Ordinal))
        {
            return;
        }

        if ((await ReferencesAsync(unit, cancellationToken)).HasReferences)
        {
            throw new DomainValidationException("code",
                $"De code {unit.Code} is al gebruikt in opdrachten, prijzen of klantinstellingen en kan niet meer gewijzigd worden. Wijzig de naam, of maak een nieuwe eenheid aan en zet deze inactief.");
        }
    }

    public async Task<ReferenceReport> ReferencesAsync(UnitType unit, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        // Codes on orders / goods / order price lines are stored uppercased; activity price lines and
        // legacy rows may differ in case — compare case-insensitively so nothing escapes the count.
        var code = unit.Code.ToUpperInvariant();
        return new ReferenceReport()
            .Add("orders", await db.TransportOrders.CountAsync(o => o.TenantId == tenantId && o.QuantityUnitCode != null && o.QuantityUnitCode.ToUpper() == code, cancellationToken),
                "opdracht", "opdrachten")
            .Add("cargo", await db.CargoItems.CountAsync(c => c.TenantId == tenantId && c.QuantityUnitCode != null && c.QuantityUnitCode.ToUpper() == code, cancellationToken),
                "goederenlijn", "goederenlijnen")
            .Add("priceLines", await db.TransportOrderPricingLines.CountAsync(l => l.TenantId == tenantId && l.Unit != null && l.Unit.ToUpper() == code, cancellationToken)
                               + await db.DossierActivityPriceLines.CountAsync(l => l.TenantId == tenantId && l.Unit != null && l.Unit.ToUpper() == code, cancellationToken),
                "prijslijn", "prijslijnen")
            .Add("priceRules", await db.PriceRules.CountAsync(r => r.TenantId == tenantId && r.UnitTypeId == unit.Id, cancellationToken)
                               + await db.CombinedUnitDiscountUnits.CountAsync(u => u.TenantId == tenantId && u.UnitTypeId == unit.Id, cancellationToken)
                               + await db.ScheduledPriceAdjustments.CountAsync(a => a.TenantId == tenantId && a.UnitTypeIdFilter == unit.Id, cancellationToken),
                "tariefregel", "tariefregels")
            .Add("serviceOptions", await db.ServiceOptions.CountAsync(o => o.TenantId == tenantId && o.UnitTypeId == unit.Id, cancellationToken),
                "service-optie", "service-opties")
            .Add("customerUnits", await db.CustomerPreferredUnits.CountAsync(u => u.TenantId == tenantId && u.UnitTypeId == unit.Id, cancellationToken),
                "klanteenheid", "klanteenheden")
            .Add("salesCategories", await db.SalesCategories.CountAsync(s => s.TenantId == tenantId && s.DefaultUnitCode != null && s.DefaultUnitCode.ToUpper() == code, cancellationToken),
                "verkoopcategorie", "verkoopcategorieën");
    }
}

public sealed class VehicleCategoryReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<VehicleCategory>
{
    public string Subject => "Deze voertuigcategorie";

    public async Task<ReferenceReport> ReferencesAsync(VehicleCategory category, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("vehicles", await db.Vehicles.CountAsync(v => v.TenantId == tenant.TenantId && v.CategoryId == category.Id, cancellationToken),
                "voertuig", "voertuigen")
            .Add("maintenancePolicies", await db.MaintenancePolicies.CountAsync(
                    p => p.TenantId == tenant.TenantId && p.CategoryId == category.Id, cancellationToken),
                "onderhoudsregel", "onderhoudsregels");
}

public sealed class TrailerCategoryReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<TrailerCategory>
{
    public string Subject => "Deze opleggercategorie";

    public async Task<ReferenceReport> ReferencesAsync(TrailerCategory category, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("trailers", await db.Trailers.CountAsync(t => t.TenantId == tenant.TenantId && t.CategoryId == category.Id, cancellationToken),
                "oplegger", "opleggers");
}

public sealed class CustomerCategoryReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<CustomerCategory>
{
    public string Subject => "Deze klantcategorie";

    public async Task<ReferenceReport> ReferencesAsync(CustomerCategory category, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("customers", await db.Customers.CountAsync(c => c.TenantId == tenant.TenantId && c.CategoryId == category.Id, cancellationToken),
                "klant", "klanten");
}

public sealed class DriverCategoryReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<DriverCategory>
{
    public string Subject => "Deze chauffeurscategorie";

    public async Task<ReferenceReport> ReferencesAsync(DriverCategory category, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("drivers", await db.Drivers.CountAsync(d => d.TenantId == tenant.TenantId && d.DriverCategoryId == category.Id, cancellationToken)
                            + await db.DriverDriverCategories.CountAsync(
                                l => l.TenantId == tenant.TenantId && l.DriverCategoryId == category.Id, cancellationToken),
                "chauffeur", "chauffeurs");
}

public sealed class DepartmentReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<Department>
{
    public string Subject => "Deze afdeling";

    public async Task<ReferenceReport> ReferencesAsync(Department department, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("employees", await db.Employees.CountAsync(e => e.TenantId == tenant.TenantId && e.DepartmentId == department.Id, cancellationToken),
                "medewerker", "medewerkers");
}

public sealed class JobFunctionReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<JobFunction>
{
    public string Subject => "Deze functie";

    public async Task<ReferenceReport> ReferencesAsync(JobFunction function, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("employees", await db.Employees.CountAsync(
                    e => e.TenantId == tenant.TenantId && e.JobFunctions.Any(f => f.JobFunctionId == function.Id), cancellationToken),
                "medewerker", "medewerkers");
}

public sealed class IssuedItemCategoryReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<IssuedItemCategory>
{
    public string Subject => "Deze artikelcategorie";

    public async Task<ReferenceReport> ReferencesAsync(IssuedItemCategory category, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("templates", await db.IssuedItemTemplates.CountAsync(
                    t => t.TenantId == tenant.TenantId && t.CategoryId == category.Id, cancellationToken),
                "artikel", "artikelen");
}
