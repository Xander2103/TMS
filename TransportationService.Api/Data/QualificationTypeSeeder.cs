using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Data;

/// <summary>
/// Global (not tenant-owned) qualification-type catalogue: driving licences, Code 95, ADR, …
/// Every qualification an employee holds points at one of these rows, so the catalogue must exist
/// in EVERY environment — until production hardening it was only created by the Development-only
/// <see cref="MasterDataSeeder"/>, leaving a production database without any qualification type.
/// Add-if-missing by <see cref="QualificationType.Code"/>: rows an administrator renamed or
/// deactivated are never touched. Reference data only — no tenant, person or demo record.
/// </summary>
public static class QualificationTypeSeeder
{
    private static readonly (string Code, string Name, string Category, bool RequiresExpiryDate)[] Defaults =
    [
        (QualificationTypeCodes.DrivingLicenceB, "Rijbewijs B", "Rijbewijs", true),
        (QualificationTypeCodes.DrivingLicenceC, "Rijbewijs C", "Rijbewijs", true),
        (QualificationTypeCodes.DrivingLicenceCE, "Rijbewijs CE", "Rijbewijs", true),
        (QualificationTypeCodes.Code95, "Code 95", "Certificaat", true),
        (QualificationTypeCodes.Adr, "ADR", "Certificaat", true),
        (QualificationTypeCodes.MedicalFitness, "Medische keuring", "Keuring", true),
        (QualificationTypeCodes.DriverCard, "Bestuurderskaart", "Certificaat", true),
        (QualificationTypeCodes.CraneCertificate, "Kraancertificaat", "Certificaat", true),
        (QualificationTypeCodes.ForkliftCertificate, "Vorkheftruckcertificaat", "Certificaat", true),
        (QualificationTypeCodes.Alfapass, "Alfapass", "Certificaat", true),
        (QualificationTypeCodes.Other, "Overig", "Overig", false),
    ];

    /// <summary>The codes this seeder guarantees (exposed for tests and the provisioning report).</summary>
    public static IReadOnlyList<string> Codes { get; } = Defaults.Select(d => d.Code).ToList();

    /// <returns>The number of rows added (0 when the catalogue was already complete).</returns>
    public static async Task<int> SyncAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existing = (await dbContext.QualificationTypes
                .Select(t => t.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (code, name, category, requiresExpiryDate) in Defaults)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            dbContext.QualificationTypes.Add(new QualificationType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Category = category,
                RequiresExpiryDate = requiresExpiryDate,
                IsActive = true,
            });
            added++;
        }

        if (added > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return added;
    }
}
