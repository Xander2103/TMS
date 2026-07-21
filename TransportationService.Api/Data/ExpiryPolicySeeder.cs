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
