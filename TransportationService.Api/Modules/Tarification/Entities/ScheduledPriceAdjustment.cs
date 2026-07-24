using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

public enum ScheduledAdjustmentStatus
{
    /// <summary>Future versions are materialized; display shows "Actief" once the date passes.</summary>
    Scheduled = 0,

    /// <summary>Cancelled before activation: future versions removed, source windows restored.</summary>
    Cancelled = 1,
}

/// <summary>
/// A bulk future price change for one customer (e.g. +4% from 01/10/2026). Confirming the
/// adjustment immediately materializes new effective-dated versions of the selected rules and
/// closes the current versions the day before — current prices stay untouched until the
/// effective date, history is preserved, and the whole change is traceable and cancellable
/// while it has not activated yet.
/// </summary>
public class ScheduledPriceAdjustment : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Signed percentage: +4.00 = increase, -2.50 = decrease.</summary>
    public decimal Percent { get; set; }

    public ScheduledAdjustmentStatus Status { get; set; } = ScheduledAdjustmentStatus.Scheduled;
    public string? Reason { get; set; }

    public List<ScheduledPriceAdjustmentRule> Rules { get; set; } = new();
}

/// <summary>Links a source rule to its created future version, so cancellation can undo both.</summary>
public class ScheduledPriceAdjustmentRule : AuditableTenantEntity
{
    public Guid AdjustmentId { get; set; }
    public Guid SourcePriceRuleId { get; set; }
    public Guid CreatedPriceRuleId { get; set; }

    /// <summary>The source rule's EffectiveUntil before it was closed; restored on cancel.</summary>
    public DateOnly? SourceOriginalEffectiveUntil { get; set; }
}
