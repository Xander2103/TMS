namespace TransportationService.Api.Tests.TestSupport;

/// <summary>Minimal controllable TimeProvider for deterministic token/expiry tests.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
