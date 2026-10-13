using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Seeds the requested default expiry-reminder policies per tenant (add-if-missing, keyed by
/// (kind, code)). Existing per-tenant customisations are preserved; only missing rows are added.
/// </summary>
public static class ExpiryPolicySeeder
{
    private sealed record Seed(ExpiryReminderTargetKind Kind, string Code, int LeadDays, bool NotifyEmployee, bool NotifyHr, bool NotifyFleetManager);

    private static readonly Seed[] Defaults =
    [
        // Qualification types (codes from QualificationTypeCodes).
        new(ExpiryReminderTargetKind.QualificationType, "DrivingLicenceB", 60, true, true, false),
        new(ExpiryReminderTargetKind.QualificationType, "DrivingLicenceC", 60, true, true, false),
        new(ExpiryReminderTargetKind.QualificationType, "DrivingLicenceCE", 60, true, true, false),
        new(ExpiryReminderTargetKind.QualificationType, "DriverCard", 60, true, true, false),
        new(ExpiryReminderTargetKind.QualificationType, "Code95", 365, true, true, false),
        new(ExpiryReminderTargetKind.QualificationType, "MedicalFitness", 60, true, true, false),
        // Employee document categories.
        new(ExpiryReminderTargetKind.EmployeeDocumentCategory, "IdentityCardFront", 30, true, true, false),
        // Tachograph calibration.
        new(ExpiryReminderTargetKind.TachographCalibration, "*", 60, false, true, true),
    ];

    public static async Task SeedAsync(TransportationDbContext dbContext)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync();
        foreach (var tenantId in tenantIds)
        {
            var existing = await dbContext.ExpiryReminderPolicies
                .Where(p => p.TenantId == tenantId)
                .Select(p => new { p.TargetKind, p.TargetCode })
                .ToListAsync();
            var existingKeys = existing.Select(e => (e.TargetKind, e.TargetCode)).ToHashSet();

            foreach (var seed in Defaults)
            {
                if (existingKeys.Contains((seed.Kind, seed.Code)))
                {
                    continue;
                }

                dbContext.ExpiryReminderPolicies.Add(new ExpiryReminderPolicy
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TargetKind = seed.Kind,
                    TargetCode = seed.Code,
                    LeadTimeDays = seed.LeadDays,
                    NotifyEmployee = seed.NotifyEmployee,
                    NotifyHr = seed.NotifyHr,
                    NotifyFleetManager = seed.NotifyFleetManager,
                    IsActive = true,
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }
}

/// <summary>
/// Seeds a starter set of issued-item templates per tenant (add-if-missing by name). Based on
/// the business example; tenants can edit/extend/remove them in Settings afterwards.
/// </summary>
public static class IssuedItemTemplateSeeder
{
    private sealed record Seed(string Name, string Category, bool Serial, bool ReturnRequired);

    private static readonly Seed[] Defaults =
    [
        new("Toegangsbadge", "Algemeen", false, true),
        new("Sleutel locker", "Algemeen", false, true),
        new("Druksleutel", "Algemeen", false, true),
        new("T-shirts", "Algemeen", false, false),
        new("Sweaters", "Algemeen", false, false),
        new("PDA Zebra/CAT", "Chauffeur & Magazijn", true, true),
        new("PBM", "Chauffeur & Magazijn", false, true),
        new("Veiligheidsschoenen", "Chauffeur & Magazijn", false, false),
        new("Total-kaart", "Chauffeur", true, true),
        new("DKV-kaart", "Chauffeur", true, true),
        new("Tolbadge", "Chauffeur", true, true),
        new("Eurotunnel-kaart", "Chauffeur", true, true),
        new("Spanbanden", "Chauffeur", false, true),
        new("Afstandsbediening laadklep", "Chauffeur", true, true),
        new("Antislipmatten", "Chauffeur", false, true),
        new("Pilotenkoffer", "Klasse 7", false, true),
        new("Chauffeursmap", "Klasse 7", false, true),
        new("Dosimeter", "Klasse 7", true, true),
        new("Mobiele telefoon", "Optioneel", true, true),
        new("Simkaart", "Optioneel", true, true),
    ];

    public static async Task SeedAsync(TransportationDbContext dbContext)
    {
        var tenantIds = await dbContext.Tenants.Select(t => t.Id).ToListAsync();
        foreach (var tenantId in tenantIds)
        {
            var hasAny = await dbContext.IssuedItemTemplates.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId);
            if (hasAny)
            {
                continue; // seed once; never re-add after tenant curation
            }

            var order = 0;
            foreach (var seed in Defaults)
            {
                dbContext.IssuedItemTemplates.Add(new Modules.Employees.Entities.IssuedItemTemplate
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = seed.Name,
                    Category = seed.Category,
                    RequiresSerialNumber = seed.Serial,
                    ReturnRequired = seed.ReturnRequired,
                    IsActive = true,
                    SortOrder = order++,
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
