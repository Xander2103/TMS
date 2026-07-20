using TransportationService.Api.Common;
using TransportationService.Api.Modules.Fleet.Services;

namespace TransportationService.Api.Tests.Fleet;

public class FleetFieldRulesTests
{
    // Explicit currentYear values keep these tests valid in any calendar year.
    [Fact]
    public void ConstructionYear_CurrentYearAndPast_AreAccepted()
    {
        FleetFieldRules.ValidateConstructionYear(2026, currentYear: 2026);
        FleetFieldRules.ValidateConstructionYear(1990, currentYear: 2026);
        FleetFieldRules.ValidateConstructionYear(null, currentYear: 2026);
    }

    [Fact]
    public void ConstructionYear_InTheFuture_IsRejectedWithFieldError()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => FleetFieldRules.ValidateConstructionYear(2027, currentYear: 2026));
        Assert.Contains("year", ex.FieldErrors!.Keys);
        Assert.Contains("2026", ex.Message);
    }

    [Fact]
    public void ConstructionYear_Before1900_IsRejected()
    {
        Assert.Throws<DomainValidationException>(
            () => FleetFieldRules.ValidateConstructionYear(1850, currentYear: 2026));
    }

    [Fact]
    public void Volume_IsComputedFromCompleteDimensions()
    {
        var (volume, isManual) = FleetFieldRules.ResolveVolume(13.6m, 2.48m, 2.7m, requestedVolume: null, requestedManual: false);
        Assert.Equal(91.066m, volume);
        Assert.False(isManual);
    }

    [Fact]
    public void Volume_ManualOverride_KeepsSuppliedValue()
    {
        var (volume, isManual) = FleetFieldRules.ResolveVolume(13.6m, 2.48m, 2.7m, requestedVolume: 85m, requestedManual: true);
        Assert.Equal(85m, volume);
        Assert.True(isManual);
    }

    [Fact]
    public void Volume_ResetToAutomatic_RecomputesFromDimensions()
    {
        // The same payload with the manual flag off recomputes — this is the "reset" path.
        var (volume, isManual) = FleetFieldRules.ResolveVolume(2m, 2m, 2m, requestedVolume: 85m, requestedManual: false);
        Assert.Equal(8m, volume);
        Assert.False(isManual);
    }

    [Fact]
    public void Volume_WithIncompleteDimensions_IsNeverInvented()
    {
        var (volume, _) = FleetFieldRules.ResolveVolume(13.6m, null, 2.7m, requestedVolume: null, requestedManual: false);
        Assert.Null(volume);

        // A supplied value survives when dimensions are incomplete (no silent data loss).
        var (kept, isManual) = FleetFieldRules.ResolveVolume(null, null, null, requestedVolume: 42m, requestedManual: false);
        Assert.Equal(42m, kept);
        Assert.False(isManual);
    }

    [Fact]
    public void Volume_Negative_IsRejectedWithFieldError()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => FleetFieldRules.ResolveVolume(null, null, null, requestedVolume: -1m, requestedManual: true));
        Assert.Contains("volumeM3", ex.FieldErrors!.Keys);
    }
}
