using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using Xunit;

namespace TransportationService.Api.Tests.Eligibility;

public class DriverEligibilityEvaluatorTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly DateOnly End = new(2026, 8, 3);
    private readonly DriverEligibilityEvaluator _sut = new();

    private static QualificationSnapshot Valid(string code, DateOnly? expiry = null) =>
        new(code, QualificationStatus.Valid, expiry ?? End.AddYears(1));

    private static DriverEligibilityRequest RequestFor(string? licence = null, bool code95 = false, bool adr = false, bool medical = false, bool crane = false, IReadOnlyList<string>? additional = null) =>
        new(licence, code95, adr, medical, crane, additional ?? [], Start, End);

    [Fact]
    public void LicenceB_DoesNotSatisfy_RequiredC()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceB")], RequestFor(licence: "C"));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("C"));
    }

    [Fact]
    public void LicenceC_DoesNotAutomaticallySatisfy_RequiredCE()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceC")], RequestFor(licence: "CE"));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void CeVehicleCombination_RequiresDrivingLicenceCE_Specifically()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceCE")], RequestFor(licence: "CE"));

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void ExpiredAdrCertificate_Blocks_AdrTransport()
    {
        var expired = new QualificationSnapshot("ADR", QualificationStatus.Expired, Start.AddDays(-1));

        var result = _sut.Evaluate([expired], RequestFor(adr: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("ADR"));
    }

    [Fact]
    public void CertificateExpiringBeforeTransportEndDate_IsInvalid_ForThatTransport()
    {
        var expiresDuringTransport = Valid("ADR", expiry: Start.AddDays(1)); // expires before End (Aug 3)

        var result = _sut.Evaluate([expiresDuringTransport], RequestFor(adr: true));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MissingMedicalFitness_Blocks_DriverEligibility()
    {
        var result = _sut.Evaluate([], RequestFor(medical: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("medische"));
    }

    [Fact]
    public void ValidCe_Code95_AndMedicalFitness_PassTheAppropriateCheck()
    {
        var result = _sut.Evaluate(
            [Valid("DrivingLicenceCE"), Valid("Code95"), Valid("MedicalFitness")],
            RequestFor(licence: "CE", code95: true, medical: true));

        Assert.True(result.IsEligible);
        Assert.Empty(result.BlockingReasons);
    }

    [Fact]
    public void SuspendedQualification_IsInvalid()
    {
        var suspended = new QualificationSnapshot("ADR", QualificationStatus.Suspended, End.AddYears(1));

        var result = _sut.Evaluate([suspended], RequestFor(adr: true));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MissingQualification_ProducesClearReason()
    {
        var result = _sut.Evaluate([], RequestFor(crane: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("kraancertificaat"));
    }

    [Fact]
    public void CraneOperation_RequiresValidCraneCertificate()
    {
        var result = _sut.Evaluate([Valid("CraneCertificate")], RequestFor(crane: true));

        Assert.True(result.IsEligible);
    }
}
