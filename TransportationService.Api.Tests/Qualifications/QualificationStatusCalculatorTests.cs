using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using Xunit;

namespace TransportationService.Api.Tests.Qualifications;

public class QualificationStatusCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 17);
    private readonly QualificationStatusCalculator _sut = new();

    private static EmployeeQualification Qualification(QualificationStatus status, DateOnly? expiryDate) =>
        new() { Status = status, ExpiryDate = expiryDate, ObtainedDate = Today.AddYears(-1) };

    [Fact]
    public void Suspended_StaysSuspended_RegardlessOfDates()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Suspended, Today.AddYears(1)), Today, 30);
        Assert.Equal(QualificationStatus.Suspended, result);
    }

    [Fact]
    public void NoExpiryDate_IsValid()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, null), Today, 30);
        Assert.Equal(QualificationStatus.Valid, result);
    }

    [Fact]
    public void ExpiryDateInPast_IsExpired()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(-1)), Today, 30);
        Assert.Equal(QualificationStatus.Expired, result);
    }

    [Fact]
    public void ExpiryDateWithinWarningWindow_IsExpiringSoon()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(10)), Today, 30);
        Assert.Equal(QualificationStatus.ExpiringSoon, result);
    }

    [Fact]
    public void ExpiryDateBeyondWarningWindow_IsValid()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(90)), Today, 30);
        Assert.Equal(QualificationStatus.Valid, result);
    }

    [Fact]
    public void PendingQualification_StaysPending_EvenIfExpiryFarInFuture()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Pending, Today.AddYears(1)), Today, 30);
        Assert.Equal(QualificationStatus.Pending, result);
    }
}
