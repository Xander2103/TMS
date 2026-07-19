namespace TransportationService.Api.Common.Scheduling;

/// <summary>
/// Shared three-level severity for schedule conflicts. Blocking stops the operation
/// (unless explicitly overridden by a permission-holder); Warning informs but never blocks;
/// Information is context that needs no action.
/// </summary>
public enum ConflictSeverity
{
    Information,
    Warning,
    Blocking,
}
