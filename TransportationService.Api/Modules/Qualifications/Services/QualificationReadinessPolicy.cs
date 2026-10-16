using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Qualifications.Services;

/// <summary>
/// HR acceptance H4/H6/P2-8: ONE rule set for what a driver's qualification records mean for
/// deployability, shared by driver readiness, the trip conflict check and the eligibility
/// evaluator. Pure and dependency-free.
/// <list type="bullet">
/// <item>A withdrawn record (<see cref="QualificationStatus.Suspended"/>, "Ingetrokken") and a
/// rejected one are history: they neither block nor satisfy — exactly what the withdraw dialog
/// promises ("telt dan niet meer mee voor inzetbaarheid").</item>
/// <item>Per qualification type the BEST live record counts: a valid replacement satisfies the
/// requirement even when an older duplicate is expired or withdrawn.</item>
/// <item>Core driver qualifications (driving licence, Code 95, medical fitness, driver card) are
/// always relevant; an expired one blocks. Situational ones (ADR, crane, forklift, Alfapass, …)
/// only block where the work requires them; elsewhere their expiry is a warning.</item>
/// <item>Without any live driving-licence record the system cannot call the driver deployable:
/// the outcome is "incomplete", never "geen belemmeringen". The completeness catalogue already
/// treats the driving licence as the deployment-critical driver requirement.</item>
/// </list>
/// </summary>
public static class QualificationReadinessPolicy
{
    public sealed record Record(string TypeCode, string TypeName, QualificationStatus EffectiveStatus, DateOnly? ExpiryDate);

    public enum Outcome { Satisfied, ExpiringSoon, Unverified, Expired }

    /// <summary>The verdict for one qualification type, from its best live record.</summary>
    public sealed record TypeVerdict(string TypeCode, string TypeName, Outcome Outcome, bool IsCore, DateOnly? ExpiryDate);

    public sealed record Summary(IReadOnlyList<TypeVerdict> Types, bool HasLiveDrivingLicence);

    private static readonly HashSet<string> DrivingLicenceCodes = new(StringComparer.Ordinal)
    {
        QualificationTypeCodes.DrivingLicenceB, QualificationTypeCodes.DrivingLicenceC, QualificationTypeCodes.DrivingLicenceCE,
    };

    private static readonly HashSet<string> CoreCodes = new(StringComparer.Ordinal)
    {
        QualificationTypeCodes.DrivingLicenceB, QualificationTypeCodes.DrivingLicenceC, QualificationTypeCodes.DrivingLicenceCE,
        QualificationTypeCodes.Code95, QualificationTypeCodes.MedicalFitness, QualificationTypeCodes.DriverCard,
    };

    public static bool IsDrivingLicence(string typeCode) => DrivingLicenceCodes.Contains(typeCode);

    public static bool IsCore(string typeCode) => CoreCodes.Contains(typeCode);

    /// <summary>Withdrawn/rejected records are history only.</summary>
    public static bool IsLive(QualificationStatus status) =>
        status is not (QualificationStatus.Suspended or QualificationStatus.Rejected);

    public static bool IsUsable(QualificationStatus status) =>
        status is QualificationStatus.Valid or QualificationStatus.ExpiringSoon;

    public static Summary Evaluate(IEnumerable<Record> records)
    {
        var verdicts = records
            .Where(r => IsLive(r.EffectiveStatus))
            .GroupBy(r => r.TypeCode, StringComparer.Ordinal)
            .Select(group =>
            {
                var best = group
                    .OrderBy(r => Rank(r.EffectiveStatus))
                    .ThenByDescending(r => r.ExpiryDate ?? DateOnly.MaxValue)
                    .First();
                return new TypeVerdict(best.TypeCode, best.TypeName, ToOutcome(best.EffectiveStatus), IsCore(best.TypeCode), best.ExpiryDate);
            })
            .OrderBy(v => v.TypeName, StringComparer.CurrentCulture)
            .ToList();

        // A lower licence category that expired is not a gap while a higher one is usable.
        var usableLicence = verdicts.Any(v => IsDrivingLicence(v.TypeCode) && v.Outcome is Outcome.Satisfied or Outcome.ExpiringSoon);
        if (usableLicence)
        {
            verdicts = verdicts
                .Where(v => !(IsDrivingLicence(v.TypeCode) && v.Outcome == Outcome.Expired))
                .ToList();
        }

        var hasLiveLicence = verdicts.Any(v => IsDrivingLicence(v.TypeCode) && v.Outcome != Outcome.Expired);
        return new Summary(verdicts, hasLiveLicence);
    }

    /// <summary>
    /// Is the requirement for <paramref name="typeCode"/> met for work ending on
    /// <paramref name="plannedEnd"/>? Any one usable live record that is still valid then suffices.
    /// </summary>
    public static bool Satisfies(IEnumerable<Record> records, string typeCode, DateOnly plannedEnd) =>
        records.Any(r => r.TypeCode == typeCode
                         && IsUsable(r.EffectiveStatus)
                         && (r.ExpiryDate is not { } expiry || expiry >= plannedEnd));

    private static int Rank(QualificationStatus status) => status switch
    {
        QualificationStatus.Valid => 0,
        QualificationStatus.ExpiringSoon => 1,
        QualificationStatus.Pending => 2,
        _ => 3, // Expired
    };

    private static Outcome ToOutcome(QualificationStatus status) => status switch
    {
        QualificationStatus.Valid => Outcome.Satisfied,
        QualificationStatus.ExpiringSoon => Outcome.ExpiringSoon,
        QualificationStatus.Pending => Outcome.Unverified,
        _ => Outcome.Expired,
    };

    /// <summary>Human-readable verdicts for driver readiness / planning surfaces.</summary>
    public sealed record Assessment(
        IReadOnlyList<string> Blocking, IReadOnlyList<string> Warnings, bool Incomplete,
        IReadOnlyList<string> BlockingTypeNames, IReadOnlyList<string> WarningLabels);

    public const string MissingLicenceMessage =
        "Geen geldig rijbewijs geregistreerd: inzetbaarheid kan niet worden bepaald.";

    public static Assessment Assess(Summary summary)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();
        var blockingNames = new List<string>();
        var warningLabels = new List<string>();
        foreach (var verdict in summary.Types)
        {
            switch (verdict.Outcome)
            {
                case Outcome.Expired when verdict.IsCore:
                    blocking.Add($"{verdict.TypeName} is verlopen.");
                    blockingNames.Add(verdict.TypeName);
                    break;
                case Outcome.Expired:
                    // Situational (ADR, kraan, …): only work that requires it is blocked — the
                    // trip conflict check does that per trip.
                    warnings.Add($"{verdict.TypeName} is verlopen (enkel vereist voor opdrachten die dit vragen).");
                    warningLabels.Add($"{verdict.TypeName} (verlopen)");
                    break;
                case Outcome.ExpiringSoon:
                    warnings.Add($"{verdict.TypeName} verloopt binnenkort.");
                    warningLabels.Add($"{verdict.TypeName} (verloopt binnenkort)");
                    break;
                case Outcome.Unverified:
                    warnings.Add($"{verdict.TypeName} is nog niet bevestigd.");
                    warningLabels.Add($"{verdict.TypeName} (in behandeling)");
                    break;
            }
        }

        var incomplete = !summary.HasLiveDrivingLicence;
        if (incomplete)
        {
            warnings.Insert(0, MissingLicenceMessage);
            warningLabels.Insert(0, "geen rijbewijs geregistreerd");
        }

        return new Assessment(blocking, warnings, incomplete, blockingNames, warningLabels);
    }
}
