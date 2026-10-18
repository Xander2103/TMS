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
}

internal static class LookupGuardText
{
    public const string DeactivateHint = "Zet het record inactief: de historiek blijft leesbaar en het kan niet meer gekozen worden voor nieuw werk.";
}

/// <summary>
/// Units are stored by CODE on goods lines, order lines, price lines and sales categories, so a
/// referenced unit is never deleted and the code of a deleted unit is never handed out again.
/// </summary>
public sealed class UnitTypeReferenceGuard(TransportationDbContext db, ITenantContext tenant) : ILookupReferenceGuard<UnitType>
{
    public string Subject => "Deze eenheid";

    public bool ReservesDeletedCodes => true;

    public async Task<ReferenceReport> ReferencesAsync(UnitType unit, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var code = unit.Code;
        return new ReferenceReport()
            .Add("orders", await db.TransportOrders.CountAsync(o => o.TenantId == tenantId && o.QuantityUnitCode == code, cancellationToken),
                "opdracht", "opdrachten")
            .Add("cargo", await db.CargoItems.CountAsync(c => c.TenantId == tenantId && c.QuantityUnitCode == code, cancellationToken),
                "goederenlijn", "goederenlijnen")
            .Add("priceLines", await db.TransportOrderPricingLines.CountAsync(l => l.TenantId == tenantId && l.Unit == code, cancellationToken)
                               + await db.DossierActivityPriceLines.CountAsync(l => l.TenantId == tenantId && l.Unit == code, cancellationToken),
                "prijslijn", "prijslijnen")
            .Add("priceRules", await db.PriceRules.CountAsync(r => r.TenantId == tenantId && r.UnitTypeId == unit.Id, cancellationToken)
                               + await db.CombinedUnitDiscountUnits.CountAsync(u => u.TenantId == tenantId && u.UnitTypeId == unit.Id, cancellationToken),
                "tariefregel", "tariefregels")
            .Add("salesCategories", await db.SalesCategories.CountAsync(s => s.TenantId == tenantId && s.DefaultUnitCode == code, cancellationToken),
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
