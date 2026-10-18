using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IDamageReportService
{
    Task<IReadOnlyList<DamageReportDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DamageReportDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken);

    /// <summary>Most recent reports across the fleet (dashboard feed).</summary>
    Task<IReadOnlyList<RecentDamageDto>> ListRecentAsync(int take, CancellationToken cancellationToken);

    Task<DamageOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateDamageReportRequest request, CancellationToken cancellationToken);

    Task<DamageOperationResult> CreateForTrailerAsync(Guid trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken);

    /// <summary>EX-03: the report created from an incident/afwijking (one per source; links back to it).</summary>
    Task<DamageOperationResult> CreateLinkedAsync(
        Guid? vehicleId, Guid? trailerId, CreateDamageReportRequest request,
        Guid? sourceIncidentId, Guid? sourceExceptionId, CancellationToken cancellationToken);

    Task<DamageOperationResult> UpdateAsync(Guid id, UpdateDamageReportRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FL-M7: attach (or replace) the photo/document of a damage report.</summary>
    Task<DamageOperationResult> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken);

    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken);

    Task<DamageOperationResult> RemoveFileAsync(Guid id, CancellationToken cancellationToken);
}
