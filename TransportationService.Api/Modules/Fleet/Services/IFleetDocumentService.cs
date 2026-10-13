using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFleetDocumentService
{
    Task<IReadOnlyList<FleetDocumentDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FleetDocumentDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken);

    /// <summary>Documents expiring within the window (or already expired), most urgent first.</summary>
    Task<IReadOnlyList<ExpiringFleetDocumentDto>> ListExpiringAsync(int withinDays, CancellationToken cancellationToken);

    Task<FleetDocumentOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateFleetDocumentRequest request, CancellationToken cancellationToken);

    Task<FleetDocumentOperationResult> CreateForTrailerAsync(Guid trailerId, CreateFleetDocumentRequest request, CancellationToken cancellationToken);

    Task<FleetDocumentOperationResult> UpdateAsync(Guid id, UpdateFleetDocumentRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<FleetDocumentOperationResult> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken);
    Task<FleetDocumentOperationResult> RemoveFileAsync(Guid id, CancellationToken cancellationToken);
}
