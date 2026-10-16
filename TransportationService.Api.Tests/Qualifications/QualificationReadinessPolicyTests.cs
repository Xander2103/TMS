using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using Xunit;
using Policy = TransportationService.Api.Modules.Qualifications.Services.QualificationReadinessPolicy;

namespace TransportationService.Api.Tests.Qualifications;

/// <summary>
/// HR acceptance 2026-10-16 H4/H6/P2-8: one rule set for what qualification records mean for
/// deployability — withdrawn records are history, a valid replacement satisfies, only expired
/// core qualifications block in general, and a driver without a live licence is "incomplete".
/// </summary>
public class QualificationReadinessPolicyTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly TripEnd = new(2026, 10, 22);

    private static Policy.Record Rec(string code, QualificationStatus status, DateOnly? expiry = null, string? name = null) =>
        new(code, name ?? code, status, expiry);

    private static readonly Policy.Record ValidLicence = Rec(QualificationTypeCodes.DrivingLicenceC, QualificationStatus.Valid, Today.AddYears(5), "Rijbewijs C");

    [Fact]
    public void ValidPlusWithdrawnDuplicate_ValidRecordSatisfies_NothingBlocks()
    {
        Policy.Record[] records =
        [
            ValidLicence,
            Rec(QualificationTypeCodes.Code95, QualificationStatus.Suspended, Today.AddDays(-20), "Code 95"),
            Rec(QualificationTypeCodes.Code95, QualificationStatus.Valid, Today.AddYears(2), "Code 95"),
        ];

        var assessment = Policy.Assess(Policy.Evaluate(records));
        Assert.Empty(assessment.Blocking);
        Assert.False(assessment.Incomplete);
        Assert.True(Policy.Satisfies(records, QualificationTypeCodes.Code95, TripEnd));

        // The trip-level evaluator agrees (it used to take the FIRST record of the type).
        var eligibility = new DriverEligibilityEvaluator().Evaluate(
            [
                new QualificationSnapshot(QualificationTypeCodes.Code95, QualificationStatus.Suspended, Today.AddDays(-20)),
                new QualificationSnapshot(QualificationTypeCodes.Code95, QualificationStatus.Valid, Today.AddYears(2)),
            ],
            new DriverEligibilityRequest(null, true, false, false, false, [], Today, TripEnd));
        Assert.True(eligibility.IsEligible);
    }

    [Fact]
    public void OnlyWithdrawn_NeitherBlocksNorSatisfies()
    {
        Policy.Record[] records = [ValidLicence, Rec(QualificationTypeCodes.Code95, QualificationStatus.Suspended, Today.AddYears(1), "Code 95")];

        var assessment = Policy.Assess(Policy.Evaluate(records));
        Assert.Empty(assessment.Blocking);
        Assert.DoesNotContain(assessment.Warnings, w => w.Contains("Code 95"));
        Assert.False(Policy.Satisfies(records, QualificationTypeCodes.Code95, TripEnd));
    }

    [Fact]
    public void ExpiredCore_Blocks_ExpiredSituational_OnlyWarns()
    {
        Policy.Record[] records =
        [
            ValidLicence,
            Rec(QualificationTypeCodes.Code95, QualificationStatus.Expired, Today.AddDays(-1), "Code 95"),
            Rec(QualificationTypeCodes.Adr, QualificationStatus.Expired, Today.AddDays(-11), "ADR"),
        ];

        var assessment = Policy.Assess(Policy.Evaluate(records));
        Assert.Equal(["Code 95"], assessment.BlockingTypeNames);
        Assert.Contains(assessment.Warnings, w => w.StartsWith("ADR is verlopen"));
        Assert.False(Policy.Satisfies(records, QualificationTypeCodes.Adr, TripEnd));
    }

    [Fact]
    public void VerifiedValid_IsSatisfied_AndReady()
    {
        Policy.Record[] records = [ValidLicence, Rec(QualificationTypeCodes.MedicalFitness, QualificationStatus.Valid, Today.AddYears(1))];

        var assessment = Policy.Assess(Policy.Evaluate(records));
        Assert.Empty(assessment.Blocking);
        Assert.Empty(assessment.Warnings);
        Assert.False(assessment.Incomplete);
    }

    [Fact]
    public void PendingUnverified_WarnsAndDoesNotSatisfyATripRequirement()
    {
        Policy.Record[] records = [ValidLicence, Rec(QualificationTypeCodes.MedicalFitness, QualificationStatus.Pending, Today.AddYears(1), "Medische keuring")];

        var assessment = Policy.Assess(Policy.Evaluate(records));
        Assert.Empty(assessment.Blocking);
        Assert.Contains(assessment.Warnings, w => w.Contains("Medische keuring") && w.Contains("niet bevestigd"));
        Assert.False(Policy.Satisfies(records, QualificationTypeCodes.MedicalFitness, TripEnd));
    }

    [Fact]
    public void NoQualificationsAtAll_IsIncomplete_NeverGreen()
    {
        var assessment = Policy.Assess(Policy.Evaluate([]));
        Assert.True(assessment.Incomplete);
        Assert.Contains(Policy.MissingLicenceMessage, assessment.Warnings);
    }

    [Fact]
    public void OnlyExpiredOrWithdrawnLicence_IsBlockedOrIncomplete()
    {
        var expired = Policy.Assess(Policy.Evaluate([Rec(QualificationTypeCodes.DrivingLicenceC, QualificationStatus.Expired, Today.AddDays(-1), "Rijbewijs C")]));
        Assert.Contains("Rijbewijs C", expired.BlockingTypeNames);
        Assert.True(expired.Incomplete);

        var withdrawn = Policy.Assess(Policy.Evaluate([Rec(QualificationTypeCodes.DrivingLicenceC, QualificationStatus.Suspended, Today.AddYears(1))]));
        Assert.Empty(withdrawn.Blocking);
        Assert.True(withdrawn.Incomplete);
    }

    [Fact]
    public void ExpiredLowerLicence_IsNotAGap_WhileAHigherLicenceIsValid()
    {
        var assessment = Policy.Assess(Policy.Evaluate(
        [
            Rec(QualificationTypeCodes.DrivingLicenceC, QualificationStatus.Expired, Today.AddDays(-3), "Rijbewijs C"),
            Rec(QualificationTypeCodes.DrivingLicenceCE, QualificationStatus.Valid, Today.AddYears(3), "Rijbewijs CE"),
        ]));
        Assert.Empty(assessment.Blocking);
        Assert.False(assessment.Incomplete);
    }

    [Fact]
    public void Calculator_UnverifiedPastExpiry_ReadsAsExpired_WithdrawnStaysWithdrawn()
    {
        var calculator = new QualificationStatusCalculator();
        var pendingExpired = new EmployeeQualification { Status = QualificationStatus.Pending, ExpiryDate = Today.AddDays(-11) };
        var pendingValid = new EmployeeQualification { Status = QualificationStatus.Pending, ExpiryDate = Today.AddYears(1) };
        var withdrawnExpired = new EmployeeQualification { Status = QualificationStatus.Suspended, ExpiryDate = Today.AddDays(-11) };

        Assert.Equal(QualificationStatus.Expired, calculator.CalculateEffectiveStatus(pendingExpired, Today, 30));
        Assert.Equal(QualificationStatus.Pending, calculator.CalculateEffectiveStatus(pendingValid, Today, 30));
        Assert.Equal(QualificationStatus.Suspended, calculator.CalculateEffectiveStatus(withdrawnExpired, Today, 30));
    }
}
