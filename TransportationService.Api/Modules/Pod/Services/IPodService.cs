using TransportationService.Api.Modules.Pod.Dtos;
using TransportationService.Api.Modules.Pod.Entities;

namespace TransportationService.Api.Modules.Pod.Services;

public interface IPodService
{
    /// <summary>Finalises the first, immutable POD version for a stop (freezes the scan summary).</summary>
    Task<PodOperationResult> FinalizeAsync(
        Guid tripId, Guid stopId, FinalizePodRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Creates the next version with a mandatory reason; the corrected version stays intact and visible.</summary>
    Task<PodOperationResult> CorrectAsync(Guid podId, CorrectPodRequest request, CancellationToken cancellationToken);

    Task<PodDetailDto?> GetByIdAsync(Guid podId, CancellationToken cancellationToken);

    /// <summary>The current POD of a stop (null when none was finalised yet), including the version chain.
    /// <paramref name="restrictToOwnDriver"/> hides other drivers' trips from restricted users.</summary>
    Task<PodDetailDto?> GetForStopAsync(Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Adds photo evidence to the current version; superseded versions never change.</summary>
    Task<PodOperationResult> AttachPhotoAsync(
        Guid podId, PodPhotoCategory category, string fileName, string contentType, Stream content,
        bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<(Stream Content, string ContentType, string FileName)?> OpenPhotoAsync(
        Guid podId, Guid photoId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<(Stream Content, string ContentType)?> OpenSignatureAsync(
        Guid podId, bool restrictToOwnDriver, CancellationToken cancellationToken);
}
