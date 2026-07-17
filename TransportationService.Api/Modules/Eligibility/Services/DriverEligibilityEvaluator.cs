namespace TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Qualifications.Entities;

/// <summary>
/// Pure, dependency-free evaluation of the eligibility business rules.
/// No database access — callers (IDriverEligibilityService) load
/// qualifications and pass snapshots in. Kept pure so the highest-risk
/// business rules in the system can be unit tested without EF Core.
/// </summary>
public class DriverEligibilityEvaluator
{
    private static readonly IReadOnlyDictionary<string, int> LicenceRank = new Dictionary<string, int>
    {
        ["B"] = 1,
        ["C"] = 2,
        ["CE"] = 3,
    };

    public EligibilityResult Evaluate(IReadOnlyList<QualificationSnapshot> qualifications, DriverEligibilityRequest request)
    {
        var blockingReasons = new List<string>();
        var warnings = new List<string>();
        var checkedQualifications = new List<EligibilityCheckedQualification>();

        if (request.RequiredDrivingLicenceCategory is { } requiredCategory)
        {
            CheckLicenceCategory(qualifications, requiredCategory, request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresCode95)
        {
            CheckSimpleRequirement(qualifications, "Code95", "Code 95 is verplicht voor beroepsmatig vervoer.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresAdr)
        {
            CheckSimpleRequirement(qualifications, "ADR", "Een geldig ADR-certificaat is verplicht voor dit transport.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresMedicalFitness)
        {
            CheckSimpleRequirement(qualifications, "MedicalFitness", "Een geldige medische keuring is verplicht.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresCraneCertificate)
        {
            CheckSimpleRequirement(qualifications, "CraneCertificate", "Een geldig kraancertificaat is verplicht voor kraanwerkzaamheden.", request, blockingReasons, checkedQualifications);
        }

        foreach (var additionalCode in request.RequiredAdditionalQualificationCodes)
        {
            CheckSimpleRequirement(qualifications, additionalCode, $"Kwalificatie '{additionalCode}' ontbreekt of is niet geldig.", request, blockingReasons, checkedQualifications);
        }

        return new EligibilityResult(blockingReasons.Count == 0, blockingReasons, warnings, checkedQualifications);
    }

    private void CheckLicenceCategory(
        IReadOnlyList<QualificationSnapshot> qualifications, string requiredCategory, DriverEligibilityRequest request,
        List<string> blockingReasons, List<EligibilityCheckedQualification> checkedQualifications)
    {
        if (requiredCategory == "CE")
        {
            // A CE combination specifically requires the CE licence — a plain C licence does not extend to CE.
            CheckSimpleRequirement(qualifications, "DrivingLicenceCE", "Rijbewijs CE is verplicht voor deze combinatie; rijbewijs C volstaat niet.", request, blockingReasons, checkedQualifications);
            return;
        }

        var requiredRank = LicenceRank[requiredCategory];
        var holderRank = new[] { "B", "C", "CE" }
            .Where(code => IsQualificationValidForPeriod(qualifications, $"DrivingLicence{code}", request))
            .Select(code => LicenceRank[code])
            .DefaultIfEmpty(0)
            .Max();

        var satisfied = holderRank >= requiredRank;
        checkedQualifications.Add(new EligibilityCheckedQualification($"DrivingLicence{requiredCategory}", satisfied,
            satisfied ? null : $"Rijbewijs {requiredCategory} (of hoger) is verplicht; chauffeur heeft dit niet of het is niet geldig."));

        if (!satisfied)
        {
            blockingReasons.Add($"Rijbewijs {requiredCategory} is verplicht voor dit transport.");
        }
    }

    private void CheckSimpleRequirement(
        IReadOnlyList<QualificationSnapshot> qualifications, string qualificationTypeCode, string blockingMessage,
        DriverEligibilityRequest request, List<string> blockingReasons, List<EligibilityCheckedQualification> checkedQualifications)
    {
        var satisfied = IsQualificationValidForPeriod(qualifications, qualificationTypeCode, request);
        checkedQualifications.Add(new EligibilityCheckedQualification(qualificationTypeCode, satisfied, satisfied ? null : blockingMessage));

        if (!satisfied)
        {
            blockingReasons.Add(blockingMessage);
        }
    }

    private static bool IsQualificationValidForPeriod(IReadOnlyList<QualificationSnapshot> qualifications, string qualificationTypeCode, DriverEligibilityRequest request)
    {
        var match = qualifications.FirstOrDefault(q => q.QualificationTypeCode == qualificationTypeCode);
        if (match is null) return false;

        if (match.EffectiveStatus is not (QualificationStatus.Valid or QualificationStatus.ExpiringSoon)) return false;

        // A qualification that expires before the planned transport ends is not valid for that transport,
        // even if it currently reads as Valid/ExpiringSoon "today".
        if (match.ExpiryDate is { } expiry && expiry < request.PlannedEndDate) return false;

        return true;
    }
}
