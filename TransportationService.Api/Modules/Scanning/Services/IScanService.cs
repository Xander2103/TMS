using TransportationService.Api.Modules.Scanning.Dtos;

namespace TransportationService.Api.Modules.Scanning.Services;

public interface IScanService
{
    /// <summary>
    /// Records one scan against a stop and classifies it deterministically (expected, wrong
    /// item, unexpected, duplicate, over-delivery, damaged). Warnings are recorded, never
    /// silently dropped. The trip must be InProgress and the stop non-terminal.
    /// </summary>
    Task<ScanOperationResult> SubmitAsync(
        Guid tripId, Guid stopId, SubmitScanRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Manual correction: sets the absolute tally for one item/action via a delta event; reason mandatory, audited.</summary>
    Task<ScanOperationResult> CorrectAsync(
        Guid tripId, Guid stopId, ScanCorrectionRequest request, CancellationToken cancellationToken);

    /// <summary>Scan history of a trip (optionally one stop), newest first, capped at 200 rows.</summary>
    Task<ScanHistoryResult> ListAsync(
        Guid tripId, Guid? stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Expected-versus-scanned summary of one stop for the relevant scan action.</summary>
    Task<ScanSummaryResult> GetStopSummaryAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);
}
