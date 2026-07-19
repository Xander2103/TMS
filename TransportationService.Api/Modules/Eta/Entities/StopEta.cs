using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Eta.Entities;

public enum EtaSource
{
    Heuristic,
    Provider,
    DispatcherOverride,
}

public enum EtaStatus
{
    OnTime,
    AtRisk,
    Late,
}

/// <summary>
/// Current ETA of one pending stop within a trip. Deliberately separate from the planned and
/// confirmed windows (those are commitments; this is the live expectation). Overrides stick
/// until cleared; recalculations never touch them.
/// </summary>
public class StopEta : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderStopId { get; set; }

    public DateTime CurrentEta { get; set; }
    public EtaSource Source { get; set; } = EtaSource.Heuristic;
    public EtaStatus Status { get; set; } = EtaStatus.OnTime;

    public Guid? OverriddenByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Every meaningful ETA change (>= 1 minute) for one stop, oldest first.</summary>
public class StopEtaHistory : AuditableTenantEntity
{
    public Guid StopEtaId { get; set; }

    public DateTime Eta { get; set; }
    public EtaSource Source { get; set; }
    public EtaStatus Status { get; set; }
    public DateTime RecordedAt { get; set; }
    public Guid? ChangedByUserId { get; set; }
}
